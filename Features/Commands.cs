﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comfort.Common;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Trainer.Configuration;
using EFT.Trainer.Extensions;
using EFT.Trainer.UI;
using EFT.UI;
using JsonType;
using UnityEngine;
using UnityEngine.SceneManagement;

#nullable enable

namespace EFT.Trainer.Features;

internal class Commands : ToggleFeature
{
	public override string Name => "commands";

	[ConfigurationProperty(Skip = true)] // we do not want to offer save/load support for this
	public override bool Enabled { get; set; } = false;

	[ConfigurationProperty]
	public virtual float X { get; set; } = DefaultX;

	[ConfigurationProperty]
	public virtual float Y { get; set; } = DefaultY;

	public override KeyCode Key { get; set; } = KeyCode.RightAlt;

	private bool Registered { get; set; } = false;
	private const string ValueGroup = "value";
	private const string ExtraGroup = "extra";
	private const float DefaultX = 40f;
	private const float DefaultY = 20f;

	private static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Escape from Tarkov");
	private static string ConfigFile => Path.Combine(UserPath, "trainer.ini");

	private static Lazy<Feature[]> Features => new(() => FeatureFactory.GetAllFeatures().OrderBy(f => f.Name).ToArray());
	private static Lazy<ToggleFeature[]> ToggleableFeatures => new(() => FeatureFactory.GetAllToggleableFeatures().OrderByDescending(f => f.Name).ToArray());

	private static GUIStyle LabelStyle => new() {wordWrap = false, normal = {textColor = Color.white}, margin = new RectOffset(8,0,8,0), fixedWidth = 150f, stretchWidth = false};
	private static GUIStyle BoxStyle => new(GUI.skin.box) {normal = {background = Texture2D.whiteTexture, textColor = Color.white}};

	protected override void Update()
	{
		if (Registered)
		{
			base.Update();
			return;
		}

		if (!PreloaderUI.Instantiated)
			return;

		RegisterCommands();
	}

	internal abstract class SelectionContext<T>
	{
		protected SelectionContext(IFeature feature, OrderedProperty orderedProperty, float parentX, float parentY, Func<T, Picker<T>> builder)
		{
			Feature = feature;
			OrderedProperty = orderedProperty;
			Picker = builder((T)orderedProperty.Property.GetValue(feature));
				
			var position = Event.current.mousePosition;
			Picker.SetWindowPosition(parentX + LabelStyle.fixedWidth * 3 + LabelStyle.margin.left * 6, position.y + parentY - 32f);
		}

		public IFeature Feature { get; }
		public OrderedProperty OrderedProperty { get; }
		public Picker<T> Picker { get; }
		public abstract int Id { get; }
	}

	internal class ColorSelectionContext(IFeature feature, OrderedProperty orderedProperty, float parentX, float parentY) : SelectionContext<Color>(feature, orderedProperty, parentX, parentY, color => new ColorPicker(color))
	{
		public override int Id => 1;
	}

	internal class KeyCodeSelectionContext(IFeature feature, OrderedProperty orderedProperty, float parentX, float parentY) : SelectionContext<KeyCode>(feature, orderedProperty, parentX, parentY, color => new EnumPicker<KeyCode>(color))
	{
		public override int Id => 2;
	}

	private Rect _clientWindowRect;
	private ColorSelectionContext? _colorSelectionContext = null;
	private KeyCodeSelectionContext? _keyCodeSelectionContext = null;
	protected override void OnGUIWhenEnabled()
	{
		_clientWindowRect = new Rect(X, Y, 490, _clientWindowRect.height);
		_clientWindowRect = GUILayout.Window(0, _clientWindowRect, RenderFeatureWindow, "EFT Trainer", GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
		X = _clientWindowRect.x;
		Y = _clientWindowRect.y;

		HandleSelectionContext(_colorSelectionContext);

		if (HandleSelectionContext(_keyCodeSelectionContext))
			_keyCodeSelectionContext = null;
	}

	private static bool HandleSelectionContext<T>(SelectionContext<T>? context)
	{
		if (context == null) 
			return false;

		var property = context.OrderedProperty.Property;
		var picker = context.Picker;

		picker.DrawWindow(context.Id, property.Name);
		property.SetValue(context.Feature, picker.Value);

		return picker.IsSelected;
	}

	private int _selectedTabIndex = 0;
	private void RenderFeatureWindow(int id)
	{
		var fixedTabs = new[] {"[summary]"};

		var tabs = fixedTabs
			.Concat
			(
				Features
					.Value
					.Select(RenderFeatureText)
			)
			.ToArray();

		var style = new GUIStyle {wordWrap = false, normal = {textColor = Color.white}, alignment = TextAnchor.UpperLeft, fixedHeight = 1, stretchHeight = true};

		GUILayout.BeginHorizontal();
		var lastIndex = _selectedTabIndex;
		_selectedTabIndex = GUILayout.SelectionGrid(_selectedTabIndex, tabs, 1, GUILayout.Width(LabelStyle.fixedWidth));

		if (lastIndex != _selectedTabIndex)
		{
			_colorSelectionContext = null;
			_keyCodeSelectionContext = null;
		}

		GUILayout.BeginVertical(style);
		GUILayout.Space(4);

		switch (_selectedTabIndex)
		{
			case 0:
				RenderSummary();
				break;
			default:
				var feature = Features.Value[_selectedTabIndex - fixedTabs.Length];
				RenderFeature(feature);

				break;

		}
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();
		GUI.DragWindow();
	}

	private static string RenderFeatureText(Feature feature)
	{
		if (feature is not ToggleFeature toggleFeature || ConfigurationManager.IsSkippedProperty(feature, nameof(Enabled)))
			return feature.Name;

		return $"{toggleFeature.Name} is {(toggleFeature.Enabled ? "on".Green() : "off".Red())}";
	}

	private void RenderSummary()
	{
		if (GUILayout.Button("Load settings"))
			LoadSettings();

		if (GUILayout.Button("Save settings"))
			SaveSettings();
	}

	private static void SaveSettings()
	{
		ConfigurationManager.Save(ConfigFile, Features.Value);
	}

	private void LoadSettings(bool warnIfNotExists = true)
	{
		var cx = X;
		var cy = Y;

		ConfigurationManager.Load(ConfigFile, Features.Value, warnIfNotExists);
		_controlValues.Clear();

		if (!Enabled)
			return;

		X = cx;
		Y = cy;
	}

	private void RenderFeature(Feature feature)
	{
		var orderedProperties = ConfigurationManager.GetOrderedProperties(feature.GetType());

		foreach (var property in orderedProperties)
			RenderFeatureProperty(feature, property);
	}

	private static readonly Dictionary<string, string> _controlValues = [];
	private void RenderFeatureProperty(Feature feature, OrderedProperty orderedProperty)
	{
		if (!orderedProperty.Attribute.Browsable)
			return;

		var property = orderedProperty.Property;

		GUILayout.FlexibleSpace();
		GUILayout.BeginHorizontal();

		GUILayout.Label(property.Name, LabelStyle);
		GUILayout.FlexibleSpace();

		var currentValue = property.GetValue(feature);
		var currentBackgroundColor = GUI.backgroundColor;

		if (currentValue == null)
			return;
			
		var width = GUILayout.Width(LabelStyle.fixedWidth);
		var newValue = RenderFeaturePropertyAsUIComponent(feature, orderedProperty, currentValue, width);

		if (currentValue != newValue)
			property.SetValue(feature, newValue);

		var focused = GUI.GetNameOfFocusedControl();

		if (ShouldResetSelectionContext(focused, _colorSelectionContext))
			_colorSelectionContext = null;

		if (ShouldResetSelectionContext(focused, _keyCodeSelectionContext))
			_keyCodeSelectionContext = null;

		GUI.backgroundColor = currentBackgroundColor;
		GUILayout.EndHorizontal();
	}

	private object RenderFeaturePropertyAsUIComponent(IFeature feature, OrderedProperty orderedProperty, object currentValue, GUILayoutOption width)
	{
		var property = orderedProperty.Property;
		var propertyType = property.PropertyType;

		var newValue = currentValue;
		var controlName = $"{feature.Name}.{property.Name}-{propertyType.Name}";
		GUI.SetNextControlName(controlName);

		switch (propertyType.Name)
		{
			case nameof(Boolean):
				newValue = RenderBooleanProperty(currentValue, width);
				break;

			case nameof(KeyCode):
				RenderKeyCodeProperty(currentValue, controlName, feature, orderedProperty, width);
				break;

			case nameof(Single):
				newValue = RenderFloatProperty(currentValue, controlName, width);
				break;

			case nameof(Int32):
				newValue = RenderIntProperty(currentValue, width);
				break;

			case nameof(Color):
				RenderColorProperty(currentValue, controlName, feature, orderedProperty, width);
				break;

			case nameof(String):
				newValue = RenderStringProperty(currentValue, width);
				break;

			default:
				// Look for inner properties
				if (currentValue is IFeature subFeature)
				{
					var subProperties = ConfigurationManager.GetOrderedProperties(propertyType);
					var length = subProperties.Length;

					if (length > 0)
					{
						width = GUILayout.Width(LabelStyle.fixedWidth / length - length);

						foreach (var innerOrderedProperty in subProperties)
						{
							var innerProperty = innerOrderedProperty.Property;
							var innerPropertyValue = innerProperty.GetValue(subFeature);
							RenderFeaturePropertyAsUIComponent(subFeature, innerOrderedProperty, innerPropertyValue, width);
						}

						break;
					}

				}

				GUILayout.Label($"Unsupported type: {propertyType.FullName}");
				break;
		}

		return newValue;
	}

	private static bool ShouldResetSelectionContext<T>(string focused, SelectionContext<T>? context)
	{
		return !string.IsNullOrEmpty(focused)
		       && !focused.EndsWith($"-{typeof(T).Name}")
		       && context != null;
	}

	private static object RenderIntProperty(object currentValue, GUILayoutOption option)
	{
		object newValue = currentValue;

		if (int.TryParse(GUILayout.TextField(currentValue.ToString(), option), out var intValue))
			newValue = intValue;

		return newValue;
	}

	private static object RenderStringProperty(object currentValue, GUILayoutOption width)
	{
		return GUILayout.TextField(currentValue.ToString(), width);
	}

	private void RenderKeyCodeProperty(object currentValue, string controlName, IFeature feature, OrderedProperty orderedProperty, GUILayoutOption option)
	{
		if (!GUILayout.Button(currentValue.ToString(), option))
			return;

		_keyCodeSelectionContext = new KeyCodeSelectionContext(feature, orderedProperty, X, Y);
		GUI.FocusControl(controlName);
	}

	private void RenderColorProperty(object currentValue, string controlName, IFeature feature, OrderedProperty orderedProperty, GUILayoutOption option)
	{
		GUI.backgroundColor = (Color) currentValue;

		if (!GUILayout.Button(string.Empty, BoxStyle, option, GUILayout.Height(22f)))
			return;

		_colorSelectionContext = new ColorSelectionContext(feature, orderedProperty, X, Y);
		GUI.FocusControl(controlName);
	}

	private static object RenderFloatProperty(object currentValue, string controlName, GUILayoutOption width)
	{
		const string decimalSeparator = ".";
		const string altDecimalSeparator = ",";

		var culture = CultureInfo.InvariantCulture;
		var newValue = currentValue;

		if (!_controlValues.TryGetValue(controlName, out var controlText))
			controlText = currentValue.ToString();

		if (controlText != currentValue.ToString())
			GUI.backgroundColor = Color.red;

		controlText = GUILayout
			.TextField(controlText, width)
			.Replace(altDecimalSeparator, decimalSeparator);

		if (!controlText.EndsWith(decimalSeparator) && float.TryParse(controlText, NumberStyles.Float, culture, out var floatValue))
		{
			newValue = floatValue;
			controlText = newValue.ToString();
		}

		_controlValues[controlName] = controlText;
		return newValue;
	}

	private object RenderBooleanProperty(object currentValue, GUILayoutOption option)
	{
		var boolValue = (bool) currentValue;
		var newValue = GUILayout.Toggle(boolValue, string.Empty, option);
		if (newValue != boolValue)
		{
			_colorSelectionContext = null;
			_keyCodeSelectionContext = null;
		}

		return newValue;
	}

	private void RegisterCommands()
	{
		foreach(var feature in ToggleableFeatures.Value)
		{
			if (feature is Commands or GameState)
				continue;

				CreateCommand($"{feature.Name}", $"(?<{ValueGroup}>(on)|(off))", m => OnToggleFeature(feature, m));
		}

		CreateCommand("dump", Dump);
		CreateCommand("status", Status);

		CreateCommand("load", () => LoadSettings());
		CreateCommand("save", SaveSettings);

		// Load default configuration
		LoadSettings(false);
		SetupWindowCoordinates();

		Registered = true;
	}

	private void SetupWindowCoordinates()
	{
		bool needfix = false;
		X = FixCoordinate(X, Screen.width, DefaultX, ref needfix);
		Y = FixCoordinate(Y, Screen.height, DefaultY, ref needfix);

		if (needfix)
			SaveSettings();
	}

	private float FixCoordinate(float coord, float maxValue, float defaultValue, ref bool needfix)
	{
		if (coord < 0 || coord >= maxValue)
		{
			coord = defaultValue;
			needfix = true;
		}

		return coord;
	}

	private static void CreateCommand(string name, Action action)
	{
		ConsoleScreen.Processor.RegisterCommand(name, action);
	}

	private void CreateCommand(string cmdName, string pattern, Action<Match> action)
	{
#if DEBUG
			AddConsoleLog($"Registering {cmdName} command...");
#endif
		ConsoleScreen.Processor.RegisterCommand(cmdName, (string args) =>
		{
			var regex = new Regex("^" + pattern + "$");
			if (regex.IsMatch(args))
			{
				action(regex.Match(args));
			}
			else
			{
				ConsoleScreen.LogError("Invalid arguments");
			}
		});
	}

	private static string GetFeatureHelpText(ToggleFeature feature)
	{
		var toggleKey = feature.Key != KeyCode.None ? $" ({feature.Key} to toggle)" : string.Empty;
		return $"{feature.Name} is {(feature.Enabled ? "on".Green() : "off".Red())}{toggleKey}";
	}

	private void Status()
	{
		foreach (var feature in ToggleableFeatures.Value)
		{
			if (feature is Commands or GameState)
				continue;

			AddConsoleLog(GetFeatureHelpText(feature));
		}
	}

	private void Dump()
	{
		var dumpfolder = Path.Combine(UserPath, "Dumps");
		var thisDump = Path.Combine(dumpfolder, $"{DateTime.Now:yyyyMMdd-HHmmss}");

		Directory.CreateDirectory(thisDump);

		AddConsoleLog("Dumping scenes...");
		for (int i = 0; i < SceneManager.sceneCount; i++) 
		{
			var scene = SceneManager.GetSceneAt(i);
			if (!scene.isLoaded)
				continue;

			var json = SceneDumper.DumpScene(scene).ToPrettyJson();
			File.WriteAllText(Path.Combine(thisDump, GetSafeFilename($"@scene - {scene.name}.txt")), json);
		}

		AddConsoleLog("Dumping game objects...");
		foreach (var go in FindObjectsOfType<GameObject>())
		{
			if (go == null || go.transform.parent != null || !go.activeSelf) 
				continue;

			var filename = GetSafeFilename(go.name + "-" + go.GetHashCode() + ".txt");
			var json = SceneDumper.DumpGameObject(go).ToPrettyJson();
			File.WriteAllText(Path.Combine(thisDump, filename), json);
		}

		AddConsoleLog($"Dump created in {thisDump}");
	}

	private static string GetSafeFilename(string filename)
	{
		return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));  	
	}

	public void OnToggleFeature(ToggleFeature feature, Match match)
	{
		var matchGroup = match.Groups[ValueGroup];
		if (matchGroup is not {Success: true})
			return;

		feature.Enabled = matchGroup.Value switch
		{
			"on" => true,
			"off" => false,
			_ => feature.Enabled
		};
	}
}
