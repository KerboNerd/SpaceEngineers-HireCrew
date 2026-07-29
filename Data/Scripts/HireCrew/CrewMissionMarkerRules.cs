using System;

namespace HireCrew
{
    /// <summary>
    /// Pure helpers for mission HUD markers (visibility, label, screen clamp).
    /// No ModAPI — unit-tested from HireCrew.Logic.Tests.
    /// </summary>
    public static class CrewMissionMarkerRules
    {
        public static bool CanViewerSee(
            long viewerIdentityId,
            long viewerFactionIdOrZero,
            long crewOwnerKey,
            bool crewOwnerIsFaction,
            long crewOwnerIdentityId,
            long crewOwnerFactionIdOrZero)
        {
            if (viewerIdentityId == 0)
                return false;

            // Direct owner identity always sees their crew.
            if (crewOwnerIdentityId != 0 && crewOwnerIdentityId == viewerIdentityId)
                return true;
            if (!crewOwnerIsFaction && crewOwnerKey != 0 && crewOwnerKey == viewerIdentityId)
                return true;

            long viewerKey;
            bool viewerIsFaction;
            CrewOwnership.Resolve(viewerIdentityId, viewerFactionIdOrZero, out viewerKey, out viewerIsFaction);

            if (crewOwnerKey == viewerKey && crewOwnerIsFaction == viewerIsFaction)
                return true;

            if (viewerFactionIdOrZero == 0)
                return false;

            // Faction-owned roster key.
            if (crewOwnerIsFaction && crewOwnerKey == viewerFactionIdOrZero)
                return true;

            // Personal crew of a faction mate.
            if (!crewOwnerIsFaction
                && crewOwnerFactionIdOrZero != 0
                && crewOwnerFactionIdOrZero == viewerFactionIdOrZero)
                return true;

            return false;
        }

        public static string FormatLabel(string displayName, double distanceMeters)
        {
            string name = string.IsNullOrEmpty(displayName) ? "Crew" : displayName.Trim();
            int meters = (int)Math.Round(Math.Max(0.0, distanceMeters));
            return name + " · " + meters + " m";
        }

        /// <summary>
        /// Clamp RichHud center-origin pixel offset to the screen inset.
        /// When outside, projects onto the border along the ray from center (edge pin).
        /// </summary>
        public static void ClampHudOffset(
            ref float offsetX,
            ref float offsetY,
            float screenW,
            float screenH,
            float marginPx)
        {
            if (screenW < 1f) screenW = 1f;
            if (screenH < 1f) screenH = 1f;
            if (marginPx < 0f) marginPx = 0f;

            float halfW = screenW * 0.5f - marginPx;
            float halfH = screenH * 0.5f - marginPx;
            if (halfW < 1f) halfW = 1f;
            if (halfH < 1f) halfH = 1f;

            float ax = Math.Abs(offsetX);
            float ay = Math.Abs(offsetY);
            if (ax <= halfW && ay <= halfH)
                return;

            float scaleX = ax > 1e-4f ? halfW / ax : float.MaxValue;
            float scaleY = ay > 1e-4f ? halfH / ay : float.MaxValue;
            float scale = scaleX < scaleY ? scaleX : scaleY;
            if (scale < float.MaxValue)
            {
                offsetX *= scale;
                offsetY *= scale;
            }
        }

        /// <summary>
        /// Always pin to the screen edge along a camera-relative view-plane direction (+right, +up).
        /// Unlike ClampHudOffset, small directions are scaled OUT to the border (not left near center).
        /// </summary>
        public static void ClampDirToScreenEdge(
            float dirRight,
            float dirUp,
            float screenW,
            float screenH,
            float marginPx,
            out float offsetX,
            out float offsetY)
        {
            if (screenW < 1f) screenW = 1f;
            if (screenH < 1f) screenH = 1f;
            if (marginPx < 0f) marginPx = 0f;

            float halfW = screenW * 0.5f - marginPx;
            float halfH = screenH * 0.5f - marginPx;
            if (halfW < 1f) halfW = 1f;
            if (halfH < 1f) halfH = 1f;

            float ax = Math.Abs(dirRight);
            float ay = Math.Abs(dirUp);
            // Directly behind the camera: right/up ~ 0 — default to top edge.
            if (ax < 1e-4f && ay < 1e-4f)
            {
                offsetX = 0f;
                offsetY = halfH;
                return;
            }

            float scaleX = ax > 1e-4f ? halfW / ax : float.MaxValue;
            float scaleY = ay > 1e-4f ? halfH / ay : float.MaxValue;
            float scale = scaleX < scaleY ? scaleX : scaleY;
            offsetX = dirRight * scale;
            offsetY = dirUp * scale;
        }
    }
}
