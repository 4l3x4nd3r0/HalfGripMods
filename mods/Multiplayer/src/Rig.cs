using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Multiplayer
{
	/// <summary>
	/// Test support, inert unless the game is started with <c>-mprig &lt;scenario&gt;</c> (a copy of the game run headless by the author).
	/// Reads the command line only; the scenarios live in <see cref="RigScenarios"/>.
	/// </summary>
	internal static class Rig
	{
		internal static string Scenario { get; private set; }
		internal static string Dir { get; private set; }
		internal static string Label { get; private set; }

		internal static bool On => !string.IsNullOrEmpty(Scenario);

		internal static string Arg(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
				{
					return args[i + 1];
				}
			}
			return null;
		}

		internal static void Init()
		{
			Scenario = Arg("-mprig");
			Dir = Arg("-mpdir");
			Label = Arg("-mplabel") ?? "rig";
			if (On && string.IsNullOrEmpty(Dir))
			{
				Dir = Path.Combine(Path.GetTempPath(), "hg_mp_rig");
			}
			if (On)
			{
				Directory.CreateDirectory(Dir);
				Mod.Log.LogInfo("RIG scenario '" + Scenario + "' as '" + Label + "', files in " + Dir);
			}
		}

		internal static string SaveText()
		{
			StringBuilder text = new StringBuilder();
			foreach (string table in new[] { "ints", "floats", "strings" })
			{
				System.Collections.IDictionary dictionary = (System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(SaveData), table).GetValue(null);
				foreach (string key in dictionary.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal))
				{
					text.Append(table).Append('/').Append(key).Append('=').Append(dictionary[key]).Append('\n');
				}
			}
			return text.ToString();
		}

		internal static string Fingerprint(string text)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
			}
		}

		/// <summary>A hash of the save tables as they are in memory, to prove a room leaves them as it found them.</summary>
		internal static string SaveFingerprint()
		{
			return Fingerprint(SaveText());
		}

		internal static void Log(string text)
		{
			Mod.Log.LogInfo("RIG[" + Label + "] " + text);
		}

		/// <summary>The name of a node from the scene root: <c>A/B/C</c>.</summary>
		internal static string PathOf(Transform t)
		{
			StringBuilder path = new StringBuilder(t.name);
			while (t.parent != null)
			{
				t = t.parent;
				path.Insert(0, t.name + "/");
			}
			return path.ToString();
		}

		/// <summary>Writes a scene or sub-tree as text: every object with its components and the values that matter for look and layout.</summary>
		internal static void Dump(Transform root, string file, int maxDepth = 12)
		{
			StringBuilder text = new StringBuilder();
			Walk(root, 0, maxDepth, text);
			File.WriteAllText(Path.Combine(Dir, file), text.ToString());
			Log("dumped " + root.name + " to " + file + " (" + text.Length + " chars)");
		}

		internal static void DumpScene(Scene scene, string file, int maxDepth = 12)
		{
			StringBuilder text = new StringBuilder();
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				Walk(go.transform, 0, maxDepth, text);
			}
			File.WriteAllText(Path.Combine(Dir, file), text.ToString());
			Log("dumped scene " + scene.name + " to " + file + " (" + text.Length + " chars)");
		}

		private static void Walk(Transform t, int depth, int maxDepth, StringBuilder text)
		{
			text.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]");
			foreach (Component c in t.GetComponents<Component>())
			{
				if (c == null)
				{
					text.Append("  <missing>");
					continue;
				}
				if (c is Transform && !(c is RectTransform))
				{
					continue;
				}
				text.Append("  ").Append(c.GetType().Name).Append(Detail(c));
			}
			text.AppendLine();
			if (depth < maxDepth)
			{
				for (int i = 0; i < t.childCount; i++)
				{
					Walk(t.GetChild(i), depth + 1, maxDepth, text);
				}
			}
		}

		private static string Detail(Component c)
		{
			try
			{
				RectTransform rect = c as RectTransform;
				if (rect != null)
				{
					return "(a" + V(rect.anchorMin) + V(rect.anchorMax) + " p" + V(rect.anchoredPosition) + " s" + V(rect.sizeDelta) + " pv" + V(rect.pivot) + " sc" + rect.localScale.x.ToString("0.##") + ")";
				}
				Image image = c as Image;
				if (image != null)
				{
					return "(" + (image.sprite != null ? image.sprite.name : "-") + " " + image.color + " " + image.type + ")";
				}
				TMP_Text tmp = c as TMP_Text;
				if (tmp != null)
				{
					string s = tmp.text.Replace("\n", "\\n");
					return "('" + (s.Length > 40 ? s.Substring(0, 40) : s) + "' " + (tmp.font != null ? tmp.font.name : "-") + " " + tmp.fontSize.ToString("0.#") + " " + tmp.color + " " + tmp.alignment + ")";
				}
				Button button = c as Button;
				if (button != null)
				{
					StringBuilder b = new StringBuilder("(int=" + button.interactable + " " + button.transition);
					int n = button.onClick.GetPersistentEventCount();
					for (int i = 0; i < n; i++)
					{
						b.Append(" ->").Append(button.onClick.GetPersistentTarget(i) != null ? button.onClick.GetPersistentTarget(i).GetType().Name : "null").Append('.').Append(button.onClick.GetPersistentMethodName(i));
					}
					return b.Append(")").ToString();
				}
				CanvasGroup group = c as CanvasGroup;
				if (group != null)
				{
					return "(alpha=" + group.alpha + " int=" + group.interactable + " rays=" + group.blocksRaycasts + ")";
				}
				Canvas canvas = c as Canvas;
				if (canvas != null)
				{
					return "(" + canvas.renderMode + " sort=" + canvas.sortingOrder + (canvas.worldCamera != null ? " cam=" + canvas.worldCamera.name : "") + ")";
				}
				Camera camera = c as Camera;
				if (camera != null)
				{
					return "(depth=" + camera.depth + " mask=" + camera.cullingMask + " ortho=" + camera.orthographic + " enabled=" + camera.enabled + ")";
				}
				LayoutGroup layout = c as LayoutGroup;
				if (layout != null)
				{
					return "(pad " + layout.padding.left + "," + layout.padding.top + ")";
				}
			}
			catch (Exception)
			{
			}
			return "";
		}

		private static string V(Vector2 v)
		{
			return "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + ")";
		}

		/// <summary>
		/// Renders the screen into a PNG without a window: every camera into one texture, then the overlay canvases through a camera of their own.
		/// </summary>
		internal static void Shot(string file, int width = 1920, int height = 1080)
		{
			try
			{
				RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
				List<Camera> cameras = Camera.allCameras.Where(c => c.enabled && c.gameObject.activeInHierarchy).OrderBy(c => c.depth).ToList();
				Dictionary<Camera, RenderTexture> old = new Dictionary<Camera, RenderTexture>();
				foreach (Camera cam in cameras)
				{
					old[cam] = cam.targetTexture;
					cam.targetTexture = target;
				}
				GameObject holder = new GameObject("RigShotCamera");
				Camera ui = holder.AddComponent<Camera>();
				ui.clearFlags = CameraClearFlags.Depth;
				ui.cullingMask = 1 << LayerMask.NameToLayer("UI");
				ui.orthographic = true;
				ui.targetTexture = target;
				ui.depth = 1000;
				ui.enabled = false;
				List<KeyValuePair<Canvas, RenderMode>> canvases = new List<KeyValuePair<Canvas, RenderMode>>();
				foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
				{
					if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
					{
						canvases.Add(new KeyValuePair<Canvas, RenderMode>(canvas, canvas.renderMode));
						canvas.renderMode = RenderMode.ScreenSpaceCamera;
						canvas.worldCamera = ui;
						canvas.planeDistance = 1f;
					}
				}
				foreach (Camera cam in cameras)
				{
					try
					{
						cam.Render();
					}
					catch (Exception e)
					{
						Log("camera " + cam.name + " would not render: " + e.Message);
					}
				}
				Canvas.ForceUpdateCanvases();
				ui.Render();
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = target;
				Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				image.Apply();
				RenderTexture.active = previous;
				File.WriteAllBytes(Path.Combine(Dir, file), image.EncodeToPNG());
				foreach (KeyValuePair<Camera, RenderTexture> pair in old)
				{
					if (pair.Key != null)
					{
						pair.Key.targetTexture = pair.Value;
					}
				}
				foreach (KeyValuePair<Canvas, RenderMode> pair in canvases)
				{
					if (pair.Key != null)
					{
						pair.Key.renderMode = pair.Value;
					}
				}
				UnityEngine.Object.Destroy(holder);
				UnityEngine.Object.Destroy(image);
				target.Release();
				Log("screenshot " + file);
			}
			catch (Exception e)
			{
				Log("screenshot failed: " + e);
			}
		}
	}

	/// <summary>The driver: a scene-surviving object that runs the scenario as a coroutine.</summary>
	internal sealed class RigRunner : MonoBehaviour
	{
		internal static RigRunner Instance;

		internal static void Start()
		{
			if (!Rig.On || Instance != null)
			{
				return;
			}
			GameObject go = new GameObject("MpRig");
			DontDestroyOnLoad(go);
			Instance = go.AddComponent<RigRunner>();
			Instance.StartCoroutine(RigScenarios.Run(Rig.Scenario));
		}
	}

	internal static class RigScenarios
	{
		private static readonly List<string> results = new List<string>();
		private static bool failed;

		/// <summary>A check that goes into the result file the test script reads.</summary>
		internal static void Check(bool ok, string what)
		{
			results.Add((ok ? "PASS " : "FAIL ") + what);
			if (!ok)
			{
				failed = true;
			}
			Rig.Log((ok ? "PASS " : "FAIL ") + what);
			File.WriteAllLines(Path.Combine(Rig.Dir, "result_" + Rig.Label + ".txt"), results.ToArray());
		}

		internal static void Signal(string name, string content = "1")
		{
			File.WriteAllText(Path.Combine(Rig.Dir, "sig_" + name + ".txt"), content);
		}

		internal static IEnumerator WaitSignal(string name, Action<string> got, float seconds = 90f)
		{
			string path = Path.Combine(Rig.Dir, "sig_" + name + ".txt");
			float until = Time.realtimeSinceStartup + seconds;
			while (!File.Exists(path) && Time.realtimeSinceStartup < until)
			{
				yield return new WaitForSecondsRealtime(0.2f);
			}
			string value = null;
			for (int i = 0; i < 5 && value == null; i++)
			{
				try
				{
					value = File.ReadAllText(path);
				}
				catch (IOException)
				{
					value = null;
				}
				if (value == null)
				{
					yield return new WaitForSecondsRealtime(0.1f);
				}
			}
			Check(value != null, "signal " + name);
			got(value);
		}

		internal static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
		{
			float until = Time.realtimeSinceStartup + seconds;
			while (!condition() && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			Check(condition(), what);
		}

		internal static Button FindButton(string label)
		{
			foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
			{
				if (b == null || !b.gameObject.scene.IsValid() || !b.gameObject.activeInHierarchy)
				{
					continue;
				}
				TMP_Text text = b.GetComponentInChildren<TMP_Text>();
				if (text != null && string.Equals(text.text, label, StringComparison.OrdinalIgnoreCase))
				{
					return b;
				}
			}
			return null;
		}

		internal static bool Click(string label)
		{
			Button b = FindButton(label);
			Check(b != null && b.interactable, "button '" + label + "' is there and enabled");
			if (b == null || !b.interactable)
			{
				return false;
			}
			b.onClick.Invoke();
			return true;
		}

		internal static IEnumerator Run(string name)
		{
			Rig.Log("scenario " + name + " starting");
			yield return new WaitForSecondsRealtime(3f);
			switch (name)
			{
				case "host":
					yield return RigRoom.Host();
					break;
				case "client":
					yield return RigRoom.Client();
					break;
				case "idle":
					yield return RigRoom.Idle();
					break;
				case "maps":
					yield return RigRoom.Maps();
					break;
				case "unload":
					yield return RigRoom.UnloadTest();
					break;
				case "steamsmoke":
					yield return RigRoom.SteamSmoke();
					break;
				case "hostwatch":
					yield return RigRoom.HostWatch();
					break;
				case "badclient":
					yield return RigRoom.BadClient();
					break;
				case "dumpmenu":
					yield return DumpMenu();
					break;
				case "dumpgarage":
					yield return DumpGarage();
					break;
				case "mpscreen":
					yield return MpScreen();
					break;
				default:
					Rig.Log("unknown scenario " + name);
					break;
			}
			Rig.Log("scenario " + name + " finished" + (failed ? " WITH FAILURES" : " ok"));
			File.WriteAllText(Path.Combine(Rig.Dir, "done_" + Rig.Label + ".txt"), failed ? "FAIL" : "OK");
			yield return new WaitForSecondsRealtime(2f);
			Application.Quit();
		}

		internal static IEnumerator WaitForMenu()
		{
			float until = Time.realtimeSinceStartup + 60f;
			while (UnityEngine.Object.FindFirstObjectByType<MainMenu>() == null && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			yield return new WaitForSecondsRealtime(2f);
		}

		internal static IEnumerator WaitForScene(string scene, float seconds = 60f)
		{
			float until = Time.realtimeSinceStartup + seconds;
			while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			yield return new WaitForSecondsRealtime(3f);
		}

		private static string Where(string path)
		{
			GameObject go = GameObject.Find(path);
			if (go == null)
			{
				return path + ": not found (or inactive)";
			}
			RectTransform rect = go.transform as RectTransform;
			if (rect == null)
			{
				return path + ": no rect";
			}
			Vector3[] c = new Vector3[4];
			rect.GetWorldCorners(c);
			return path + ": active=" + go.activeInHierarchy + " world " + c[0] + " to " + c[2] + " scale " + rect.lossyScale.x.ToString("0.###");
		}

		private static IEnumerator MpScreen()
		{
			yield return WaitForMenu();
			Rig.Log(Where("MAIN_MENU/MenuUI"));
			Rig.Log(Where("MAIN_MENU/MenuUI/SidePanel"));
			Rig.Log(Where("MAIN_MENU/MenuUI/SidePanel/Sections"));
			Flow.OpenMultiplayerScreen();
			for (int i = 1; i <= 4; i++)
			{
				yield return new WaitForSecondsRealtime(1.5f);
				Rig.Log("t=" + (i * 1.5f) + " " + Where("MAIN_MENU/MenuUI/SidePanel"));
				Rig.Log("t=" + (i * 1.5f) + " " + Where("MAIN_MENU/MenuUI/SidePanel/Sections"));
				Rig.Log("t=" + (i * 1.5f) + " " + Where("MAIN_MENU/MenuUI/SidePanel/Sections/Multiplayer"));
				Rig.Log("t=" + (i * 1.5f) + " " + Where("MAIN_MENU/MenuUI/SidePanel/Sections/Multiplayer/Background"));
				Rig.Shot("mpscreen_" + i + ".png");
			}
			Rig.DumpScene(SceneManager.GetActiveScene(), "mpscreen_dump.txt");
			// control: the game's own Collection screen through the same path
			MainMenu menu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
			menu.CloseMultiplayer();
			yield return new WaitForSecondsRealtime(3f);
			menu.OpenCollection();
			yield return new WaitForSecondsRealtime(4f);
			Rig.Shot("control_collection.png");
			Canvas mine = GameObject.Find("MAIN_MENU/MenuUI/SidePanel/Sections/Multiplayer") != null ? GameObject.Find("MAIN_MENU/MenuUI/SidePanel/Sections/Multiplayer").GetComponent<Canvas>() : null;
			Canvas theirs = GameObject.Find("MAIN_MENU/MenuUI/SidePanel/Sections/Collection") != null ? GameObject.Find("MAIN_MENU/MenuUI/SidePanel/Sections/Collection").GetComponent<Canvas>() : null;
			Rig.Log("canvas mine: " + (mine == null ? "none" : mine.renderMode + " override=" + mine.overrideSorting + " order=" + mine.sortingOrder + " layer=" + mine.sortingLayerName) + "; collection: " + (theirs == null ? "none" : theirs.renderMode + " override=" + theirs.overrideSorting + " order=" + theirs.sortingOrder + " layer=" + theirs.sortingLayerName));
		}

		private static IEnumerator DumpGarage()
		{
			yield return WaitForMenu();
			SaveSandbox.Enter();
			RunState.StartRun();
			Rig.Log("stand-in run started: active=" + RunState.Active + " selection=" + RunState.HasSelection);
			SceneLoader.Instance.LoadUpgradeMenu(0.2f);
			yield return WaitForScene("SCENE_UI_UpgradeMenu");
			Rig.Log("garage scene is " + SceneManager.GetActiveScene().name);
			Rig.DumpScene(SceneManager.GetActiveScene(), "garage_dump.txt");
			Rig.Shot("garage.png");
		}

		private static IEnumerator DumpMenu()
		{
			float until = Time.realtimeSinceStartup + 40f;
			while (UnityEngine.Object.FindFirstObjectByType<MainMenu>() == null && Time.realtimeSinceStartup < until)
			{
				yield return null;
			}
			yield return new WaitForSecondsRealtime(3f);
			Rig.DumpScene(SceneManager.GetActiveScene(), "menu_dump.txt");
			Rig.Shot("menu.png");
		}
	}
}
