using TMPro;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// What the multiplayer adds to the garage scene when it is opened from the MULTIPLAYER screen: the shop, wallet and run map are
	/// gone (they are not part of multiplayer) and a line of the game's own font says how to get out. Races are started from the room,
	/// not from here.
	/// </summary>
	internal sealed class GarageUi : MonoBehaviour
	{
		private static GarageUi instance;

		private Canvas canvas;

		internal static void Install(MenuManager menu)
		{
			if (instance != null)
			{
				Destroy(instance.gameObject);
			}
			HideShop();
			GameObject go = new GameObject("MpGarageUi");
			instance = go.AddComponent<GarageUi>();
			instance.Build();
			Mod.Log.LogInfo("Garage ready (multiplayer loadout).");
		}

		/// <summary>No shop, no money, no run map: they are not part of multiplayer.</summary>
		private static void HideShop()
		{
			string[] names = { "MoneyContainer", "ShopLock", "RewardsContainer", "MapTab", "ItemUnlock" };
			GameObject found = GameObject.Find("UI/MenuCanvas");
			if (found == null)
			{
				Mod.Log.LogWarning("The garage's UI/MenuCanvas was not found; the shop stays visible.");
				return;
			}
			foreach (string name in names)
			{
				Transform t = found.transform.Find(name);
				if (t != null)
				{
					t.gameObject.SetActive(false);
				}
				else
				{
					Mod.Log.LogWarning("Garage object not found to hide: " + name);
				}
			}
		}

		private void OnDestroy()
		{
			if (instance == this)
			{
				instance = null;
			}
			if (canvas != null)
			{
				Destroy(canvas.gameObject);
			}
		}

		internal static void Remove()
		{
			if (instance != null)
			{
				Destroy(instance.gameObject);
			}
		}

		private void Build()
		{
			canvas = Ui.Overlay("MpGarageCanvas", 50);
			canvas.transform.SetParent(transform, false);
			Ui.Label(canvas.transform, "MULTIPLAYER GARAGE", 44f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(900f, 60f));
			Ui.Label(canvas.transform, "Every item is yours here. Fit your car, then press ESC to go back.", 28f, Ui.Soft, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(30f, -76f), new Vector2(1000f, 44f));
		}
	}
}
