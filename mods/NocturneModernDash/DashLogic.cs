using System;

namespace NocturneModernDash
{
    // Exactly one pad input source is read per field update: the Controller
    // actions (player's key config) when the integration is active, otherwise
    // the game's own LT/RT. Never both. The P key is read in both cases, as
    // Controller's built-in Dash always did.
    internal static class DashInput
    {
        internal static bool ReadHeld(bool integrationActive, Func<bool> controllerHeld, Func<bool> standaloneHeld) =>
            integrationActive ? controllerHeld() : standaloneHeld();
    }

    // Dash acts (and reads input) only while enabled and while Controller's
    // Settings window is closed; otherwise the field update runs at vanilla speed.
    internal static class DashGate
    {
        internal static bool Allows(bool enabled, bool settingsOpen) => enabled && !settingsOpen;
    }

    // Dash / Dash Keep state, moved unchanged from Controller's built-in
    // FieldDashPatch. Pure: callers pass in the inputs, so the rules can be
    // unit tested. Sampled only from the field update (fldPlayerCalc Prefix)
    // while Dash is enabled and Settings is closed; outside that nothing
    // changes, including the Keep latch. Keep is runtime only (not saved).
    internal sealed class DashState
    {
        private bool _latched;
        private bool _comboWasHeld;

        internal bool IsKeepOn => _latched;

        // Returns true when this field update runs at dash speed. keepToggled
        // is true on the update where a Keep press (rising edge) flipped it.
        internal bool Update(bool dashHeld, bool keepComboHeld, bool keyboardHeld, out bool keepToggled)
        {
            keepToggled = false;
            if (keepComboHeld && !_comboWasHeld)
            {
                _latched = !_latched;
                keepToggled = true;
            }
            _comboWasHeld = keepComboHeld;

            return keyboardHeld || dashHeld || _latched;
        }
    }
}
