using System;
using HalfGripTools;
using UnityEngine;

namespace Multiplayer
{
	/// <summary>
	/// Lists a key in the game's Settings > Controls through <c>ctx.Keys</c> (HalfGripTools 1.0.2). It is called by reflection so this mod
	/// still compiles on an older HalfGripTools and can say what is wrong, instead of failing with a compile error.
	/// </summary>
	internal static class ModKeyShim
	{
		internal static bool Supported(ModContext ctx)
		{
			return ctx.GetType().GetProperty("Keys") != null;
		}

		/// <summary>
		/// Adds the key and returns a test for "went down this frame, even while the game has input muted" that follows the player's rebind.
		/// If the key could not be added, the test still works on the default key.
		/// </summary>
		internal static Func<bool> Add(ModContext ctx, KeyCode key, string label, Controls.Section section)
		{
			object keys = ctx.GetType().GetProperty("Keys").GetValue(ctx, null);
			object added = keys.GetType().GetMethod("Add").Invoke(keys, new object[] { key, label, section, null });
			if (added == null)
			{
				return () => Input.GetKeyDown(key);
			}
			System.Reflection.PropertyInfo raw = added.GetType().GetProperty("DownRaw");
			return () => (bool)raw.GetValue(added, null);
		}
	}
}
