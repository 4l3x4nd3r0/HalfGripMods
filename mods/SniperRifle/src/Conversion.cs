using HalfGripTools;
using System;
using System.Reflection;
using UnityEngine;

namespace HalfGripSniper
{
	/// <summary>
	/// The rifle sits where the SMG sits: the scene's gun object (its body you grab, its bolt, its aim model, its rack and its magazines)
	/// is reused as scenery and wiring. What it does is not: the SMG's gun component and its magazines are swapped for the rifle's own
	/// <see cref="SniperGun"/> and <see cref="Cartridge"/>, which carry over only the authored settings.
	/// </summary>
	internal static class Conversion
	{
		/// <summary>Called as the SMG's gun component wakes, with the rifle equipped. Puts a <see cref="SniperGun"/> on the same object and switches the SMG's off.</summary>
		public static void ConvertGun(NAutomaticGun smg)
		{
			SniperGun rifle = smg.gameObject.AddComponent<SniperGun>();
			rifle.Setup(smg);
			// Start and Update do not run on a disabled component, and nothing else in the game talks to the SMG's gun directly.
			smg.enabled = false;
			Mod.Log.LogInfo("Replaced the SMG's gun component on '" + smg.name + "' with the sniper rifle");
		}

		/// <summary>Called as the rack wakes. Every SMG magazine in it becomes a cartridge, before the rack counts and copies them.</summary>
		public static void ConvertMagazines(MagazineManager rack)
		{
			Sprite look = null;
			int converted = 0;
			foreach (NMagazine magazine in rack.GetComponentsInChildren<NMagazine>(true))
			{
				GameObject go = magazine.gameObject;
				Cartridge cartridge = go.AddComponent<Cartridge>();
				GunConversion.CopyFields(typeof(Grabbable), magazine, cartridge);
				GunConversion.CopyFields(typeof(FallingObject), magazine, cartridge);
				GunConversion.CopyFields(typeof(NAmmo), magazine, cartridge);
				cartridge.loadedSprite = magazine.loadedSprite;
				cartridge.emptySprite = magazine.emptySprite;
				cartridge.insertSounds = magazine.insertSounds;

				foreach (SpriteRenderer renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (renderer.sprite == null)
					{
						continue;
					}
					Sprite replacement = Mod.Ctx.Sprites.LoadSprite("sniper_round.png", renderer.sprite);
					if (replacement != null)
					{
						look = replacement;
						renderer.sprite = replacement;
					}
				}

				// The SMG magazine's component must go before the rack looks for ammo, or it would count this one twice.
				UnityEngine.Object.DestroyImmediate(magazine);
				cartridge.ResetRestPose(go.transform.localPosition, go.transform.localRotation);
				converted++;
			}
			if (converted > 0)
			{
				// A magazine the car is given (an item, a pickup, a kill) is worth two cartridges, not one.
				rack.entitiesPerMagazine = 2;
			}
			Mod.Log.LogInfo($"Rack '{rack.name}': {converted} magazines became cartridges (sprite {(look ? look.name : "none")})");
		}
	}
}
