using UnityEngine;

namespace HalfGripRpg
{
	/// <summary>
	/// The RPG's own explosion, smoke and sounds, made in code, with textures drawn in code too. The game's explosion and boss rocket
	/// are only reachable through scenery that is not loaded in a race, and its hit-dust material is too small to read at this scale.
	/// </summary>
	internal static class Vfx
	{
		private static Material softMaterial;

		private static Material ringMaterial;

		private static AudioClip boom;

		private static AudioClip launch;

		private static Texture2D Disc(int size, System.Func<float, float> alphaAtRadius)
		{
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.HideAndDontSave
			};
			var pixels = new Color32[size * size];
			float half = (size - 1) * 0.5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
					byte a = (byte)(Mathf.Clamp01(alphaAtRadius(r)) * 255f);
					pixels[y * size + x] = new Color32(255, 255, 255, a);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			return texture;
		}

		private static Material MakeMaterial(Texture2D texture, string name)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				Mod.Log.LogWarning("No Sprites/Default shader in this build, so the RPG's particles cannot be drawn");
				return null;
			}
			var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
			material.mainTexture = texture;
			return material;
		}

		// A soft round puff: solid in the middle, fading out to the edge.
		private static Material Soft()
		{
			if (softMaterial == null)
			{
				softMaterial = MakeMaterial(Disc(64, r => Mathf.Pow(1f - r, 1.4f) * 1.25f), "RpgPuff");
			}
			return softMaterial;
		}

		// A thin bright ring, for the shockwave.
		private static Material Ring()
		{
			if (ringMaterial == null)
			{
				ringMaterial = MakeMaterial(Disc(128, r => Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.09f, 2f))), "RpgRing");
			}
			return ringMaterial;
		}

		public static AudioClip BoomClip()
		{
			if (boom == null)
			{
				boom = Synth("rpg boom", 3.2f, (t, noise, state) =>
				{
					// A sharp crack, then a rumble that loses its top end as it dies, over a deep thump.
					float crack = noise * Mathf.Exp(-t * 55f) * 1.6f;
					float cutoff = Mathf.Lerp(0.45f, 0.012f, Mathf.Clamp01(t / 1.8f));
					state[0] += (noise - state[0]) * cutoff;
					float rumble = state[0] * 3.2f * Mathf.Exp(-t * 1.5f);
					float thump = Mathf.Sin(6.2831853f * (62f - 30f * Mathf.Min(t, 1.2f)) * t) * Mathf.Exp(-t * 3.2f) * 1.3f;
					return crack + rumble + thump;
				});
			}
			return boom;
		}

		public static AudioClip LaunchClip()
		{
			if (launch == null)
			{
				launch = Synth("rpg launch", 1.4f, (t, noise, state) =>
				{
					// The charge goes off with a bang, then the motor hisses and roars as the rocket leaves.
					float bang = noise * Mathf.Exp(-t * 40f) * 1.4f + Mathf.Sin(6.2831853f * 85f * t) * Mathf.Exp(-t * 12f);
					state[0] += (noise - state[0]) * 0.22f;
					float roar = state[0] * (1f - Mathf.Exp(-t * 25f)) * Mathf.Exp(-t * 2.2f) * 2.2f;
					return bang + roar;
				});
			}
			return launch;
		}

		private delegate float Voice(float t, float noise, float[] state);

		private static AudioClip Synth(string name, float seconds, Voice voice)
		{
			const int rate = 44100;
			int count = (int)(rate * seconds);
			var data = new float[count];
			var state = new float[2];
			var rng = new System.Random(name.GetHashCode());
			for (int i = 0; i < count; i++)
			{
				float t = i / (float)rate;
				float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
				// A few milliseconds of fade in so it starts without a click, and a fade out so it ends without one.
				float edge = Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((seconds - t) / 0.15f);
				data[i] = (float)System.Math.Tanh(voice(t, noise, state) * edge);
			}
			AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
			clip.SetData(data, 0);
			clip.hideFlags = HideFlags.HideAndDontSave;
			return clip;
		}

		private static Gradient Ramp(params (float at, Color colour)[] stops)
		{
			var colours = new GradientColorKey[stops.Length];
			var alphas = new GradientAlphaKey[stops.Length];
			for (int i = 0; i < stops.Length; i++)
			{
				colours[i] = new GradientColorKey(new Color(stops[i].colour.r, stops[i].colour.g, stops[i].colour.b), stops[i].at);
				alphas[i] = new GradientAlphaKey(stops[i].colour.a, stops[i].at);
			}
			var gradient = new Gradient();
			gradient.SetKeys(colours, alphas);
			return gradient;
		}

		private static ParticleSystem MakeSystem(Transform parent, string name, Material material)
		{
			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			ParticleSystem ps = go.AddComponent<ParticleSystem>();
			// A new system starts playing with defaults; stop it before changing anything that cannot change while it plays.
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			ParticleSystem.MainModule main = ps.main;
			main.playOnAwake = false;
			main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			ParticleSystem.EmissionModule emission = ps.emission;
			emission.rateOverTime = 0f;
			ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
			renderer.sharedMaterial = material;
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			return ps;
		}

		private static void Grow(ParticleSystem ps, float from, float to)
		{
			ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
			grow.enabled = true;
			grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from), new Keyframe(0.35f, Mathf.Lerp(from, to, 0.7f)), new Keyframe(1f, to)));
		}

		// A one-off puff of particles from a point or a ball.
		private static ParticleSystem Puff(Transform parent, string name, Material material, int count, float life, float speed, float size, Gradient colour, float gravity, float spawnRadius)
		{
			ParticleSystem ps = MakeSystem(parent, name, material);
			ParticleSystem.MainModule main = ps.main;
			main.loop = false;
			main.duration = 0.2f;
			main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.65f, life);
			main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.25f, speed);
			main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
			main.gravityModifier = gravity;
			main.maxParticles = count * 2;
			ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.radius = Mathf.Max(0.01f, spawnRadius);
			ParticleSystem.ColorOverLifetimeModule over = ps.colorOverLifetime;
			over.enabled = true;
			over.color = colour;
			Grow(ps, 0.5f, 1.2f);
			return ps;
		}

		/// <summary>
		/// A flash, a shockwave, a rising fireball, a boiling column of smoke, a spray of sparks, a fire that burns on for a few
		/// seconds and a burst of light. Everything is sized from the length of a car: the shockwave runs out to two car lengths,
		/// which is as far as the blast does full damage.
		/// </summary>
		public static void Explosion(Vector3 at, float carLength)
		{
			var root = new GameObject("RpgExplosion");
			root.transform.position = at;
			Material soft = Soft();
			Material ring = Ring();
			if (soft == null)
			{
				Object.Destroy(root, 1f);
				return;
			}
			float r = carLength * 1.3f;

			// The white-hot flash at the instant it goes off.
			Puff(root.transform, "flash", soft, 1, 0.2f, 0f, r * 2.8f,
				Ramp((0f, new Color(1f, 1f, 0.9f, 1f)), (1f, new Color(1f, 0.7f, 0.3f, 0f))), 0f, 0.01f).Play();

			// The shockwave: a ring that races out to two car lengths.
			if (ring != null)
			{
				ParticleSystem wave = Puff(root.transform, "shockwave", ring, 1, 0.55f, 0f, carLength * 4f,
					Ramp((0f, new Color(1f, 0.95f, 0.85f, 0.9f)), (1f, new Color(1f, 0.85f, 0.6f, 0f))), 0f, 0.01f);
				ParticleSystem.SizeOverLifetimeModule size = wave.sizeOverLifetime;
				size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.4f, 0.75f), new Keyframe(1f, 1f)));
				wave.Play();
			}

			// The fireball: big, hot, and carried upward as it burns.
			ParticleSystem fire = Puff(root.transform, "fire", soft, 90, 1.5f, r * 1.7f, r * 1.1f,
				Ramp((0f, new Color(1f, 0.96f, 0.65f, 1f)), (0.18f, new Color(1f, 0.62f, 0.12f, 1f)), (0.55f, new Color(0.65f, 0.2f, 0.06f, 0.8f)), (1f, new Color(0.15f, 0.08f, 0.06f, 0f))), 0f, r * 0.3f);
			Lift(fire, r * 1.2f);
			fire.Play();

			// A dark column of smoke that keeps rising and spreading.
			ParticleSystem smoke = Puff(root.transform, "smoke", soft, 60, 6f, r * 0.9f, r * 1.4f,
				Ramp((0f, new Color(0.3f, 0.26f, 0.23f, 0.9f)), (0.35f, new Color(0.17f, 0.16f, 0.16f, 0.8f)), (1f, new Color(0.1f, 0.1f, 0.1f, 0f))), 0f, r * 0.4f);
			Lift(smoke, r * 0.9f);
			smoke.Play();

			// A fire that goes on burning where it landed, and the smoke it sends up.
			ParticleSystem embers = Lingering(root.transform, "burning", soft, 3.2f, 35f, 0.9f, r * 0.7f, r * 0.55f,
				Ramp((0f, new Color(1f, 0.9f, 0.5f, 0.9f)), (0.4f, new Color(1f, 0.5f, 0.1f, 0.8f)), (1f, new Color(0.3f, 0.1f, 0.05f, 0f))));
			Lift(embers, r * 0.6f);
			embers.Play();
			ParticleSystem pillar = Lingering(root.transform, "pillar", soft, 4.5f, 14f, 5f, r * 0.5f, r * 0.9f,
				Ramp((0f, new Color(0.25f, 0.22f, 0.2f, 0.7f)), (0.4f, new Color(0.16f, 0.15f, 0.15f, 0.6f)), (1f, new Color(0.1f, 0.1f, 0.1f, 0f))));
			Lift(pillar, r * 0.7f);
			pillar.Play();

			ParticleSystem sparks = Puff(root.transform, "sparks", soft, 110, 1.3f, r * 6.5f, r * 0.09f,
				Ramp((0f, new Color(1f, 0.95f, 0.6f, 1f)), (1f, new Color(1f, 0.35f, 0.05f, 0f))), 1.4f, r * 0.1f);
			ParticleSystemRenderer sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
			sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
			sparkRenderer.lengthScale = 3f;
			sparkRenderer.velocityScale = 0.04f;
			sparks.Play();

			var lightObject = new GameObject("flash light");
			lightObject.transform.SetParent(root.transform, false);
			Light light = lightObject.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(1f, 0.6f, 0.25f);
			light.range = carLength * 8f;
			light.intensity = 60f;
			root.AddComponent<FadeLight>().Init(light, 0.9f);

			Object.Destroy(root, 12f);
		}

		// Pushes a system's particles upward over their lives, so a cloud rises instead of just spreading.
		private static void Lift(ParticleSystem ps, float speed)
		{
			ParticleSystem.ForceOverLifetimeModule force = ps.forceOverLifetime;
			force.enabled = true;
			force.space = ParticleSystemSimulationSpace.World;
			force.y = new ParticleSystem.MinMaxCurve(speed);
		}

		// A system that keeps emitting for a while, rather than all at once.
		private static ParticleSystem Lingering(Transform parent, string name, Material material, float seconds, float rate, float life, float speed, float size, Gradient colour)
		{
			ParticleSystem ps = MakeSystem(parent, name, material);
			ParticleSystem.MainModule main = ps.main;
			main.loop = false;
			main.duration = seconds;
			main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0f, speed);
			main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
			main.maxParticles = 600;
			ParticleSystem.EmissionModule emission = ps.emission;
			emission.rateOverTime = rate;
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.radius = size * 0.5f;
			ParticleSystem.ColorOverLifetimeModule over = ps.colorOverLifetime;
			over.enabled = true;
			over.color = colour;
			Grow(ps, 0.5f, 1.3f);
			return ps;
		}

		/// <summary>The cloud thrown out of the back of the tube when it fires.</summary>
		public static void Backblast(Vector3 at, Vector3 backward)
		{
			Material soft = Soft();
			if (soft == null)
			{
				return;
			}
			var root = new GameObject("RpgBackblast");
			root.transform.position = at;
			root.transform.rotation = Quaternion.LookRotation(backward);
			ParticleSystem ps = Puff(root.transform, "smoke", soft, 24, 1.8f, 22f, 2.2f,
				Ramp((0f, new Color(0.95f, 0.85f, 0.7f, 0.8f)), (0.3f, new Color(0.7f, 0.68f, 0.65f, 0.6f)), (1f, new Color(0.4f, 0.4f, 0.4f, 0f))), -0.05f, 0.2f);
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Cone;
			shape.angle = 16f;
			shape.radius = 0.25f;
			ps.Play();
			Object.Destroy(root, 4f);
		}

		/// <summary>
		/// What a rocket leaves in the air: a thick trail of smoke, a short bright flame at the motor, and a streak, so there is a
		/// line to follow from the tube to where it landed. <paramref name="tail"/> is the rear end of the rocket, in its own space.
		/// </summary>
		public static void Trail(Transform rocket, Vector3 tail)
		{
			Material soft = Soft();
			if (soft == null)
			{
				return;
			}
			var holder = new GameObject("trail");
			holder.transform.SetParent(rocket, false);
			holder.transform.localPosition = tail;

			// Smoke, laid down evenly along the path however fast the rocket goes.
			ParticleSystem smoke = MakeSystem(holder.transform, "smoke", soft);
			ParticleSystem.MainModule main = smoke.main;
			main.loop = true;
			main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1.2f);
			main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
			main.gravityModifier = -0.03f;
			main.maxParticles = 3000;
			ParticleSystem.EmissionModule emission = smoke.emission;
			emission.rateOverDistance = 0.9f;
			ParticleSystem.ShapeModule shape = smoke.shape;
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.radius = 0.25f;
			ParticleSystem.ColorOverLifetimeModule over = smoke.colorOverLifetime;
			over.enabled = true;
			over.color = Ramp((0f, new Color(1f, 0.85f, 0.55f, 0.9f)), (0.12f, new Color(0.85f, 0.82f, 0.8f, 0.8f)), (0.6f, new Color(0.55f, 0.55f, 0.55f, 0.45f)), (1f, new Color(0.4f, 0.4f, 0.4f, 0f)));
			Grow(smoke, 0.5f, 2.2f);
			smoke.Play();

			// The motor's flame.
			ParticleSystem flame = MakeSystem(holder.transform, "flame", soft);
			ParticleSystem.MainModule flameMain = flame.main;
			flameMain.loop = true;
			flameMain.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
			flameMain.startSpeed = 0f;
			flameMain.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
			flameMain.maxParticles = 300;
			ParticleSystem.EmissionModule flameEmission = flame.emission;
			flameEmission.rateOverDistance = 0f;
			flameEmission.rateOverTime = 160f;
			ParticleSystem.ColorOverLifetimeModule flameColour = flame.colorOverLifetime;
			flameColour.enabled = true;
			flameColour.color = Ramp((0f, new Color(1f, 0.95f, 0.7f, 1f)), (0.5f, new Color(1f, 0.55f, 0.1f, 0.9f)), (1f, new Color(0.8f, 0.2f, 0.05f, 0f)));
			flame.Play();

			// A streak behind it that is there at once, before the smoke has spread.
			TrailRenderer streak = holder.AddComponent<TrailRenderer>();
			streak.sharedMaterial = soft;
			streak.time = 0.5f;
			streak.minVertexDistance = 0.5f;
			streak.widthCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(1f, 0f));
			streak.colorGradient = Ramp((0f, new Color(1f, 0.8f, 0.4f, 0.9f)), (0.3f, new Color(0.9f, 0.5f, 0.2f, 0.5f)), (1f, new Color(0.5f, 0.5f, 0.5f, 0f)));

			var glow = new GameObject("glow");
			glow.transform.SetParent(holder.transform, false);
			Light light = glow.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = new Color(1f, 0.55f, 0.2f);
			light.range = 30f;
			light.intensity = 6f;
		}
	}

	/// <summary>Fades a light out, quickly at first.</summary>
	internal class FadeLight : MonoBehaviour
	{
		private Light target;

		private float peak;

		private float length;

		private float age;

		public void Init(Light light, float seconds)
		{
			target = light;
			peak = light.intensity;
			length = seconds;
		}

		private void Update()
		{
			age += Time.deltaTime;
			if (target == null)
			{
				return;
			}
			float left = 1f - Mathf.Clamp01(age / length);
			target.intensity = peak * left * left;
		}
	}
}
