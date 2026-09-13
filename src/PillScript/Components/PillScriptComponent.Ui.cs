using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using PillScript.Panel;
using PillScript.Scripting;

namespace PillScript.Components
{
    /// <summary>
    /// The part of the component that serves the Rhino panel: the published flag, the values the
    /// panel's controls hold, and the path from a changed value to the next solve.
    /// </summary>
    public partial class PillScriptComponent
    {
        /// <summary>
        /// A dragged slider sends a value every few milliseconds. Values arriving inside this
        /// window are collected and a single solve runs after the last of them.
        /// </summary>
        static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(60);

        readonly Dictionary<string, object> _ui =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        DispatcherTimer _uiSettle;

        /// <summary>Whether this component's controls appear in the Rhino panel.</summary>
        internal bool IsPublished { get; private set; }

        /// <summary>
        /// Whether its section in the panel is rolled up. Saved in the document, so an arrangement
        /// of several sections survives reopening the file.
        /// </summary>
        internal bool IsCollapsed { get; private set; }

        internal void SetCollapsed(bool collapsed)
        {
            if (IsCollapsed == collapsed) return;

            IsCollapsed = collapsed;
            AnnouncePublished();
        }

        /// <summary>
        /// Where this section sits in the panel. Canvas order follows the order the scripts were
        /// written in, so the panel keeps a separate order, set by dragging a section. int.MaxValue
        /// means unset, and unset sections follow the ones with a position.
        /// </summary>
        internal int UiOrder { get; private set; } = int.MaxValue;

        internal void SetOrder(int order) => UiOrder = order;

        /// <summary>How long the last solve took, in milliseconds, and whether it completed.</summary>
        internal double LastSolveMs { get; private set; }

        internal bool LastSolveFailed { get; private set; }

        internal void RecordSolve(double milliseconds, bool failed)
        {
            LastSolveMs = milliseconds;
            LastSolveFailed = failed;
        }

        /// <summary>Raised when a component's published flag or values changed. The panel listens.</summary>
        internal static event Action Published;

        internal static void AnnouncePublished() => Published?.Invoke();

        /// <summary>
        /// Calls RegisterUi on the compiled script, passing the values the controls currently
        /// hold. This runs on every publication, so an edited RegisterUi takes effect as soon as it
        /// compiles. A script with no build, or without the override, registers nothing.
        /// </summary>
        internal UiRegistrar Register()
        {
            var registrar = new UiRegistrar(UiHeld);
            UiProblem = null;

            if (!(_compiled?.Instance is ScriptBase script))
            {
                // Without this message an unbuilt script looks the same in the panel as one that
                // registered no controls.
                UiProblem = _compiled == null
                    ? "This script has not been built, so its controls are not known."
                    : null;

                return registrar;
            }

            try
            {
                script.RegisterUi(registrar);
            }
            catch (Exception exception)
            {
                // The section would otherwise go empty with no sign that RegisterUi threw.
                UiProblem = ScriptFault.Describe(ScriptFault.Unwrap(exception));
            }

            return registrar;
        }

        /// <summary>Why there are no controls: an exception message, the not-built notice, or null.</summary>
        internal string UiProblem { get; private set; }

        /// <summary>The values the panel has set, keyed by control name.</summary>
        internal IReadOnlyDictionary<string, object> UiHeld => _ui;

        internal void SetPublished(bool published)
        {
            if (IsPublished == published) return;

            RecordUndoEvent("Publish to panel");
            IsPublished = published;

            // Opened before the redraw, so the first publication in a session brings the panel up
            // with the controls already in it.
            if (published) UiPanelHost.Show();

            AnnouncePublished();
        }

        /// <summary>
        /// Takes a value from the panel and restarts the settle timer. A slider drag therefore
        /// costs one solve at the end instead of one per step.
        /// </summary>
        internal void SetUiValue(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            _ui[name] = value;

            _uiSettle?.Stop();
            _uiSettle = new DispatcherTimer(DispatcherPriority.Background) { Interval = Settle };

            _uiSettle.Tick += (_, __) =>
            {
                _uiSettle?.Stop();
                _uiSettle = null;

                ExpireSolution(true);
            };

            _uiSettle.Start();
        }

        // ----- what is kept in the document ------------------------------------------------------

        /// <summary>
        /// Only values the panel has set are written. Defaults come from RegisterUi, and storing
        /// them here as well would leave the document holding a stale default after the script's
        /// own default changed, with no way to tell the two apart.
        /// </summary>
        void WriteUi(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("UiCollapsed", IsCollapsed);
            if (UiOrder != int.MaxValue) writer.SetInt32("UiOrder", UiOrder);

            writer.SetInt32("UiCount", _ui.Count);

            var index = 0;
            foreach (var pair in _ui)
            {
                writer.SetString("UiName", index, pair.Key);

                switch (pair.Value)
                {
                    case bool flag:
                        writer.SetString("UiKind", index, "flag");
                        writer.SetBoolean("UiFlag", index, flag);
                        break;

                    case double number:
                        writer.SetString("UiKind", index, "number");
                        writer.SetDouble("UiNumber", index, number);
                        break;

                    default:
                        writer.SetString("UiKind", index, "text");
                        writer.SetString("UiText", index,
                            Convert.ToString(pair.Value,
                                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }

                index++;
            }
        }

        void ReadUi(GH_IO.Serialization.GH_IReader reader)
        {
            _ui.Clear();

            if (reader.ItemExists("UiCollapsed")) IsCollapsed = reader.GetBoolean("UiCollapsed");
            if (reader.ItemExists("UiOrder")) UiOrder = reader.GetInt32("UiOrder");

            var count = reader.ItemExists("UiCount") ? reader.GetInt32("UiCount") : 0;

            for (var index = 0; index < count; index++)
            {
                var name = reader.GetString("UiName", index);
                var kind = reader.GetString("UiKind", index);

                switch (kind)
                {
                    case "flag": _ui[name] = reader.GetBoolean("UiFlag", index); break;
                    case "number": _ui[name] = reader.GetDouble("UiNumber", index); break;
                    default: _ui[name] = reader.GetString("UiText", index); break;
                }
            }
        }

        /// <summary>The optional stylesheet a script project may carry to restyle the panel.</summary>
        internal const string StyleFile = "ui.css";

        /// <summary>
        /// A saved ui.css reaches the panel immediately. Styling needs neither a compile nor a
        /// solve, so the panel is redrawn as soon as the file is written.
        /// </summary>
        internal void NoteSaved(string file)
        {
            if (IsPublished && string.Equals(file, StyleFile, StringComparison.OrdinalIgnoreCase))
                AnnouncePublished();
        }

        /// <summary>Everything the panel needs to draw this component, read in one call.</summary>
        internal (UiRegistrar Ui, string Problem, string Style) Publication()
            => (Register(), UiProblem, Project.Find(StyleFile)?.Content ?? string.Empty);

        /// <summary>
        /// Clears button values after the solve their press triggered, so the next solve does not
        /// see the same press.
        /// </summary>
        internal void ReleaseButtons(UiRegistrar registrar)
        {
            var pressed = registrar.Buttons.Where(name => _ui.ContainsKey(name)).ToList();
            if (pressed.Count == 0) return;

            foreach (var name in pressed) _ui.Remove(name);
        }
    }
}
