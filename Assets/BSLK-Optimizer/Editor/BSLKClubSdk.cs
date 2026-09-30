using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BSLK.Optimizer
{
    // Optional integration: the optimizer also imports into a plain Unity project.
    internal static class BSLKClubSdk
    {
        internal sealed class Stats
        {
            public long triangles, slots, particles, collisions, lights;
            public double textureMegabytes;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        private static object Member(object instance, string name)
        {
            if (instance == null) throw new InvalidOperationException("SDK не вернул " + name);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            FieldInfo field = instance.GetType().GetField(name, flags);
            if (field != null) return field.GetValue(instance);
            PropertyInfo property = instance.GetType().GetProperty(name, flags);
            if (property != null) return property.GetValue(instance);
            throw new MissingMemberException(instance.GetType().FullName, name);
        }

        internal static bool TryRead(GameObject avatar, out Stats result, out string reason)
        {
            result = null;
            reason = "VRChat SDK не установлен или эта версия API не поддерживается.";
            try
            {
                Type statsType = FindType("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats");
                Type calculator = FindType("VRC.SDKBase.Validation.Performance.AvatarPerformance");
                if (statsType == null || calculator == null) return false;
                MethodInfo method = calculator.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m =>
                    m.Name == "CalculatePerformanceStats" && m.GetParameters().Length == 4 &&
                    m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType == typeof(GameObject) &&
                    m.GetParameters()[2].ParameterType == statsType && m.GetParameters()[3].ParameterType == typeof(bool));
                if (method == null) return false;
                // The SDK control panel normally initializes these resources. Our window may
                // be opened before that panel (and batch-mode tests never open the panel).
                statsType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                object stats = Activator.CreateInstance(statsType, new object[] { false });
                method.Invoke(null, new object[] { avatar.name, avatar, stats, false });
                // Do not turn missing/nullable stats into a false zero/pass.
                object physics = Member(stats, "physBone");
                object collision = Member(physics, "collisionCheckCount");
                object triangles = Member(stats, "polyCount"), slots = Member(stats, "materialCount");
                object particles = Member(stats, "particleTotalCount"), lights = Member(stats, "lightCount");
                object memory = Member(stats, "textureMegabytes");
                if (new[] { collision, triangles, slots, particles, lights, memory }.Any(x => x == null))
                    throw new InvalidOperationException("SDK не заполнил все показатели.");
                result = new Stats
                {
                    triangles = Convert.ToInt64(triangles), slots = Convert.ToInt64(slots),
                    particles = Convert.ToInt64(particles), collisions = Convert.ToInt64(collision),
                    lights = Convert.ToInt64(lights), textureMegabytes = Convert.ToDouble(memory)
                };
                reason = "";
                return true;
            }
            catch (Exception e)
            {
                reason = "Не удалось получить статистику SDK: " + (e.InnerException ?? e).Message;
                return false;
            }
        }
    }
}
