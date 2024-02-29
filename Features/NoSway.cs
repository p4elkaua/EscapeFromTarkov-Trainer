using EFT.Trainer.Extensions;
using JetBrains.Annotations;
using UnityEngine;

#nullable enable

namespace EFT.Trainer.Features;

[UsedImplicitly]
internal class NoSway : ToggleFeature
{
	public override string Name => "nosway";

	public override bool Enabled { get; set; } = false;

	protected override void UpdateWhenEnabled()
	{
		var player = GameState.Current?.LocalPlayer;
		if (!player.IsValid())
			return;

		var weaponAnimation = player.ProceduralWeaponAnimation;
		if (weaponAnimation == null)
			return;

        weaponAnimation.motionReact.Intensity = 0f;
        weaponAnimation.Breath.Intensity = 0f;

        weaponAnimation.HandsContainer.HandsRotation.Current.x = 0f;
        weaponAnimation.HandsContainer.HandsRotation.Current.y = 0f;
        weaponAnimation.HandsContainer.HandsRotation.Current.z = 0f;
        weaponAnimation.HandsContainer.HandsPosition.Current.x = 0f;
        weaponAnimation.HandsContainer.HandsPosition.Current.y = 0f;
        weaponAnimation.HandsContainer.HandsPosition.Current.z = 0f;

	}
}
