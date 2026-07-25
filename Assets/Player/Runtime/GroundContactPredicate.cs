namespace SubwaySurfers.Player.Domain
{
    public static class GroundContactPredicate
    {
        public static bool IsValid(
            int contactLayer,
            bool hasRunningSurfaceMarker,
            bool isTrigger,
            float contactDistance,
            float supportNormalUp,
            int groundLayerMask,
            float contactTolerance,
            float normalThreshold)
        {
            if (contactLayer < 0 || contactLayer > 31) return false;

            var isGroundLayer = (groundLayerMask & (1 << contactLayer)) != 0;
            return isGroundLayer &&
                hasRunningSurfaceMarker &&
                !isTrigger &&
                contactDistance <= contactTolerance &&
                supportNormalUp >= normalThreshold;
        }
    }
}
