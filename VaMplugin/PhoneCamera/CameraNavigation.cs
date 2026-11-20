using System;
using UnityEngine;

namespace MVRPlugin
{
    // Navigation is intentionally independent from ARCore. All Unity transforms
    // are written on the main thread, never from the UDP receiver/encoder worker.
    public partial class PhoneController
    {
        private sealed class NavigationPacket
        {
            public uint session, sequence;
            public int instance, revision, frame, flags;
            public float yaw, pitch, panX, panY, logDolly, fov;
            public double received;
        }

        private volatile NavigationPacket navigationInput;
        private NavigationPacket navigationPrevious;
        private bool navigating;
        private int navigationRevision, navigationFrameSerial;
        private Vector3 navigationFrameCenter, navigationFollowOrigin, navigationPan;
        private Quaternion navigationRotation;
        private float navigationDistance, navigationFov;

        // Called only after the camera worker has verified the source endpoint,
        // session number, target instance and revision.
        private void ReceiveNavigation(byte[] bytes, double received)
        {
            if (bytes.Length != 52) return;
            NavigationPacket command = new NavigationPacket();
            command.session = BitConverter.ToUInt32(bytes, 4);
            command.instance = BitConverter.ToInt32(bytes, 8);
            command.revision = BitConverter.ToInt32(bytes, 12);
            command.sequence = BitConverter.ToUInt32(bytes, 16);
            command.frame = BitConverter.ToInt32(bytes, 20);
            command.flags = BitConverter.ToInt32(bytes, 24);
            command.yaw = BitConverter.ToSingle(bytes, 28);
            command.pitch = BitConverter.ToSingle(bytes, 32);
            command.panX = BitConverter.ToSingle(bytes, 36);
            command.panY = BitConverter.ToSingle(bytes, 40);
            command.logDolly = BitConverter.ToSingle(bytes, 44);
            command.fov = BitConverter.ToSingle(bytes, 48);
            command.received = received;
            if (!ValidNavigationNumber(command.yaw) || !ValidNavigationNumber(command.pitch)
                || !ValidNavigationNumber(command.panX) || !ValidNavigationNumber(command.panY)
                || !ValidNavigationNumber(command.logDolly) || !ValidNavigationNumber(command.fov)
                || command.fov < 10f || command.fov > 110f
                || (command.flags & ~3) != 0) return;
            navigationInput = command; // atomic reference publish
        }

        private static bool ValidNavigationNumber(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 1000000f;
        }

        // Return true when navigation owns the capture camera transform.
        // When false, the original ARCore ApplyPose path remains untouched.
        private bool ApplyNavigationPose()
        {
            NavigationPacket command = navigationInput;
            Worker active = worker;
            if (capture == null || active == null || command == null
                || command.session != active.run || command.instance != targetInstance
                || command.revision != targetRevision || targetAtom == null)
            {
                if (navigating) LeaveNavigation();
                return false;
            }

            if ((command.flags & 1) == 0)
            {
                if (navigating) LeaveNavigation();
                return false;
            }

            bool newTarget = !navigating || navigationRevision != targetRevision;
            bool shouldFrame = command.frame != navigationFrameSerial || frameObjectRequested;
            if (newTarget || shouldFrame)
            {
                frameObjectRequested = false;
                ResetGrabView();
                // Reuse exactly the existing framing / renderer-bounds implementation.
                FrameContainingObject(false, Quaternion.identity);
                navigationPan = Vector3.zero;
                navigationRotation = capture.transform.rotation;
                navigationDistance = Mathf.Max(.05f,
                    Vector3.Distance(capture.transform.position, navigationFrameCenter));
                navigationFov = Mathf.Clamp(command.fov, 10f, 110f);
                navigationFollowOrigin = NavigationTargetOrigin();
                navigationPrevious = command;
                navigationRevision = targetRevision;
                navigationFrameSerial = command.frame;
                navigating = true;
                active.origin = null;
            }
            else if (navigationPrevious != null && command.sequence != navigationPrevious.sequence)
            {
                // Input fields are cumulative. Missing UDP packets therefore cannot
                // permanently lose part of a drag, pinch or pan gesture.
                float yaw = Mathf.Clamp(command.yaw - navigationPrevious.yaw, -90f, 90f);
                float pitch = Mathf.Clamp(command.pitch - navigationPrevious.pitch, -90f, 90f);
                navigationRotation = Quaternion.AngleAxis(yaw, Vector3.up) * navigationRotation;
                // Clamp elevation without relying on Euler angles at the poles.
                float elevation = Mathf.Asin(Mathf.Clamp(
                    Vector3.Dot(navigationRotation * Vector3.forward, Vector3.up), -.999f, .999f))
                    * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(elevation + pitch, -85f, 85f) - elevation;
                navigationRotation = navigationRotation * Quaternion.AngleAxis(-pitch, Vector3.right);

                float height = 2f * navigationDistance * Mathf.Tan(navigationFov * Mathf.Deg2Rad * .5f);
                float aspect = capture.aspect;
                float dx = Mathf.Clamp(command.panX - navigationPrevious.panX, -2f, 2f);
                float dy = Mathf.Clamp(command.panY - navigationPrevious.panY, -2f, 2f);
                navigationPan += navigationRotation * (Vector3.right * (dx * height * aspect)
                    + Vector3.up * (dy * height));
                float dolly = Mathf.Clamp(command.logDolly - navigationPrevious.logDolly, -2f, 2f);
                navigationDistance = Mathf.Clamp(navigationDistance * Mathf.Exp(dolly), .05f, 200f);
                navigationFov = Mathf.Clamp(command.fov, 10f, 110f);
                navigationPrevious = command;
            }

            Vector3 follow = (command.flags & 2) != 0
                ? NavigationTargetOrigin() - navigationFollowOrigin : Vector3.zero;
            Vector3 pivot = navigationFrameCenter + follow + navigationPan;
            capture.transform.rotation = navigationRotation;
            capture.transform.position = pivot - (navigationRotation * Vector3.forward) * navigationDistance;
            capture.fieldOfView = navigationFov;
            return true;
        }

        private Vector3 NavigationTargetOrigin()
        {
            if (targetAtom == null) return Vector3.zero;
            return targetAtom.mainController != null
                ? targetAtom.mainController.transform.position : targetAtom.transform.position;
        }

        private void LeaveNavigation()
        {
            navigating = false;
            navigationPrevious = null;
            if (worker != null) worker.origin = null;
            reference = 0;
            ResetGrabView();
        }
    }
}
