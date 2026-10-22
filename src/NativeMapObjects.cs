using System;
using UnityEngine;

namespace LegacyMapExpansion
{
    internal static class NativeMapObjects
    {
        private static ParticleSystem impactParticles;
        private static bool impactLoaded;

        internal static void Prepare(GameObject prefab)
        {
            if (prefab.name == "City_Props_Barrier_1")
                foreach (Transform child in prefab.transform)
                {
                    if (child.name == "Destroyed" || !child.GetComponent<Collider>()) continue;
                    child.localRotation = Quaternion.identity;
                    var body = child.GetComponent<Rigidbody>();
                    if (!body) body = child.gameObject.AddComponent<Rigidbody>();
                    body.mass = 8f; body.isKinematic = true; body.useGravity = true;
                    var release = child.GetComponent<KinematicToDynamic>();
                    if (!release) release = child.gameObject.AddComponent<KinematicToDynamic>();
                    release.maxImpulce = 120f; release.Particles = ImpactParticles();
                }
            foreach (var light in prefab.GetComponentsInChildren<Light>(true))
            {
                var distance = light.GetComponent<DistanceViewLight>() ?? light.gameObject.AddComponent<DistanceViewLight>();
                distance.dist = 120f;
                if (prefab.name.StartsWith("City_Props_Lantern_", StringComparison.Ordinal))
                {
                    var cycle = light.gameObject.AddComponent<DayNight>();
                    cycle.nightLight = distance;
                    light.enabled = !DayNight.isNowDay;
                    distance.canEnabled = !DayNight.isNowDay;
                }
            }
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name != "Destroyed" || !transform.parent) continue;
                var owner = transform.parent.gameObject;
                var collider = owner.GetComponent<Collider>();
                if (!collider || collider.isTrigger || owner.GetComponent<Destruction>()) continue;
                transform.gameObject.SetActive(false);
                var destruction = owner.AddComponent<Destruction>();
                destruction.maxImpulce = prefab.name == "City_Props_Lantern_1" ? 2000f : 1000f;
                destruction.DestroedObject = transform.gameObject;
                destruction.Particles = ImpactParticles();
            }
            if (prefab.name.StartsWith("Road_signature_", StringComparison.Ordinal) || prefab.name.IndexOf("stuf", StringComparison.OrdinalIgnoreCase) >= 0)
                foreach (var body in prefab.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (!body.isKinematic || body.GetComponent<KinematicToDynamic>()) continue;
                    bool fragment = false;
                    for (var parent = body.transform; parent && parent != prefab.transform; parent = parent.parent)
                        if (parent.name == "Destroyed") { fragment = true; break; }
                    if (fragment || body.GetComponent<Destruction>()) continue;
                    var dynamic = body.gameObject.AddComponent<KinematicToDynamic>();
                    dynamic.maxImpulce = 120f;
                    dynamic.Particles = ImpactParticles();
                }
        }

        private static ParticleSystem ImpactParticles()
        {
            if (!impactLoaded)
            {
                impactLoaded = true;
                var wrapper = Resources.Load<GameObject>("Objects/9ec03b9edc027304594628bfa2ab8635");
                var link = wrapper ? wrapper.GetComponent<LinkResource>() : null;
                var stock = link ? link.resource as GameObject : null;
                var destruction = stock ? stock.GetComponent<Destruction>() : null;
                if (destruction) impactParticles = destruction.Particles;
            }
            return impactParticles;
        }
    }
}
