using UnityEngine;
using System;

namespace Fluxy
{
    [AddComponentMenu("Physics/FluXY/TargetProviders/Target Detector", 800)]
    public class FluxyTargetDetector : FluxyTargetProvider
    {
        public Vector3 size = new Vector3(0.5f, 0.5f, 0.5f);
        public int maxTargets = 4;
        public LayerMask layers = ~0;

        private Collider[] colliders = new Collider[0];
        private FluxyTarget[] targets = new FluxyTarget[0];

        public void OnValidate()
        {
            Array.Resize(ref colliders, maxTargets);
            Array.Resize(ref targets, maxTargets);
        }

        public void Awake()
        {
            Array.Resize(ref colliders, maxTargets);
            Array.Resize(ref targets, maxTargets);
        }

        public override FluxyTarget[] GetTargets(out int targetCount)
        {
            targetCount = Physics.OverlapBoxNonAlloc(transform.position, size * 0.5f, colliders, Quaternion.identity, layers);

            for (int i = 0; i < targetCount; ++i)
                colliders[i].TryGetComponent(out targets[i]);

            return targets;
        }

        public void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f,0.8f,1,0.5f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
