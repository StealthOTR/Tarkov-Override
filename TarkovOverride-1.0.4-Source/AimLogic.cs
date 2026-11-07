using System;

namespace SPTPracticeMenu
{
    public enum AimActivation { RightMouseHeld, HoldJsonHotkey, Always }
    public static class AimLogic
    {
        public static bool WaitForSwitch(bool hadTarget, bool changed, bool enabled, float seconds, float now, ref float deadline)
        {
            if (!hadTarget || !changed || !enabled || seconds <= 0) { deadline = -1; return false; }
            if (deadline < 0) deadline = now + seconds;
            if (now < deadline) return true;
            deadline = -1; return false;
        }
        public static bool InCone(float squaredDistance, float angle, float range, float fov, bool correctionEnabled, float correctionDegrees)
        {
            return squaredDistance >= 0.25f && squaredDistance <= range*range &&
                angle <= (correctionEnabled ? Math.Min(fov,correctionDegrees) : fov);
        }
        public static bool Active(AimActivation mode, bool rightMouse, bool hotkey, bool requireRightMouse)
        {
            return mode == AimActivation.Always || (mode == AimActivation.RightMouseHeld ? rightMouse :
                hotkey && (!requireRightMouse || rightMouse));
        }
        public static void Turn(float forwardX, float forwardY, float forwardZ, float targetX, float targetY,
            float targetZ, float deltaTime, float smoothSeconds, float degreesPerSecond, out float yaw, out float pitch)
        {
            const double degrees = 180.0 / Math.PI;
            double difference = (Math.Atan2(targetX, targetZ) - Math.Atan2(forwardX, forwardZ)) * degrees;
            difference = (difference + 540) % 360 - 180;
            double vertical = (Math.Asin(Math.Max(-1, Math.Min(1, forwardY))) - Math.Asin(Math.Max(-1, Math.Min(1, targetY)))) * degrees;
            double dt = Math.Max(0, Math.Min(deltaTime, 0.05));
            double blend = smoothSeconds <= 0 ? 1 : 1 - Math.Exp(-dt / smoothSeconds);
            difference *= blend; vertical *= blend;
            double length = Math.Sqrt(difference * difference + vertical * vertical);
            double limit = Math.Max(0, degreesPerSecond) * dt;
            if (length > limit && length > 0) { difference *= limit / length; vertical *= limit / length; }
            yaw = (float)difference; pitch = (float)vertical;
        }
    }
}
