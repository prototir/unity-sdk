using UnityEngine;

namespace Prototir
{
    // Separate bridge object: enabling feedback never renames a developer's scene object.
    public sealed class PrototirReviewReceiver : MonoBehaviour
    {
        internal PrototirReview Owner;
        public void OnReviewVisibility(string value) { if (Owner) Owner.OnReviewVisibility(value); }
        public void CaptureReview(string value) { if (Owner) Owner.CaptureReview(value); }
    }
}
