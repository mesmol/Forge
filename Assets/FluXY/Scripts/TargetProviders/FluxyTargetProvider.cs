using UnityEngine;

namespace Fluxy
{
    public interface IFluxyTargetProvider
    {
        FluxyTarget[] GetTargets(out int targetCount);
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(FluxyContainer))]
    public abstract class FluxyTargetProvider : MonoBehaviour, IFluxyTargetProvider
    {
        public abstract FluxyTarget[] GetTargets(out int targetCount);
    }
}
