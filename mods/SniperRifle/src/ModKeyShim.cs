using System;
using UnityEngine;
using HalfGripTools;

namespace HalfGripSniper
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

		/// <summary>Adds the key. Returns false when the key could not be added (the key itself still works on its default).</summary>
		internal static bool Add(ModContext ctx, KeyCode key, string label, Controls.Section section)
		{
			object keys = ctx.GetType().GetProperty("Keys").GetValue(ctx, null);
			object added = keys.GetType().GetMethod("Add").Invoke(keys, new object[] { key, label, section, null });
			return added != null;
		}
	}
}
