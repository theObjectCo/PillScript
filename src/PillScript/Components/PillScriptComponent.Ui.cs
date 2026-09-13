using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using PillScript.Panel;
using PillScript.Scripting;

namespace PillScript.Components
{
    /// <summary>
    /// The half of the component that belongs to the Rhino panel: whether it is published there,
    /// what the panel's controls are currently set to, and how a change becomes a new solve.
    /// </summary>
    public partial class PillScriptComponent
    {
        /// <summary>
        /// A dragged slider sends a value every few milliseconds. Solving on each one makes the
        /// canvas crawl and the last one is the only one anybody wanted, so they are collected and
        /// the solve happens once they stop.
        /// </summary>
        static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(60);

        readonly Dictionary<string, object> _ui =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        DispatcherTimer _uiSettle;

        /// <summary>Whether this component's controls appear in the Rhino panel.</summary>
        internal bool IsPublished { get; private set; }

        /// <summary>
        /// Whether its section in the panel is rolled up. Kept with the document, because a panel
        /// of four scripts is arranged once and then wants to stay that way.
        /// </summary>
        internal bool IsCollapsed { get; private set; }

        internal void SetCollapsed(bool collapsed)
        {
            if (IsCollapsed == collapsed) return;

            IsCollapsed = collapsed;
            AnnouncePublished();
        }

        /// <summary>How long the last solve took, in milliseconds, and whether it went through.</summary>
        internal double LastSolveMs { get; private set; }

        internal bool LastSolveFailed { get; private set; }

        internal void RecordSolve(double milliseconds, bool failed)
        {
            LastSolveMs = milliseconds;
            LastSolveFailed = failed;
        }

        /// <summary>Raised when any component's publication or values changed, for the panel.</summary>
        internal static event Action Published;

        internal static void AnnouncePublished() => Published?.Invoke();

        /// <summary>
        /// Asks the compiled script what controls it wants, handing it what they currently hold.
        /// Asked fresh each time, so a change shows up as soon as it is compiled. A script that
        /// has not been built, or that does not override RegisterUi, registers nothing.
        /// </summary>
        internal UiRegistrar Register()
        {
            var registrar = new UiRegistrar(UiHeld);
            UiProblem = null;

            if (!(_compiled?.Instance is ScriptBase script))
            {
                // An empty section with nothing said about it looks like a script that registered
                // nothing, when what happened is that there is no build to ask.
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
                // A panel that empties itself because RegisterUi threw helps nobody; say so.
                UiProblem = ScriptFault.Describe(ScriptFault.Unwrap(exception));
            }

            return registrar;
        }

        /// <summary>Why RegisterUi produced nothing, or null when it did not throw.</summary>
        internal string UiProblem { get; private set; }

        /// <summary>What the panel has set, which is all a control needs to be read back.</summary>
        internal IReadOnlyDictionary<string, object> UiHeld => _ui;

        internal void SetPublished(bool published)
        {
            if (IsPublished == published) return;

            RecordUndoEvent("Publish to panel");
            IsPublished = published;

            // Opened before the panel is told to redraw, so the first publication of a session
            // shows the controls rather than an empty tab somebody has to find first.
            if (published) UiPanelHost.Show();

            AnnouncePublished();
        }

        /// <summary>
        /// Takes a value from the panel. The solve is put off for a moment, so a slider being
        /// dragged is one solve at the end rather than forty along the way.
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
        /// Only what the panel has been set to is written. What a control starts at comes from
        /// RegisterUi, so writing that here as well would leave the document holding a number the
        /// script has since changed its mind about, with no way to tell which of the two was meant.
        /// </summary>
        void WriteUi(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetBoolean("UiCollapsed", IsCollapsed);
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

        /// <summary>The file a script may carry to restyle the panel.</summary>
        internal const string StyleFile = "ui.css";

        /// <summary>Everything the panel needs to draw this component, in one read.</summary>
        internal (UiRegistrar Ui, string Problem, string Style) Publication()
            => (Register(), UiProblem, Project.Find(StyleFile)?.Content ?? string.Empty);

        /// <summary>
        /// Clears the buttons once the solve their press caused is over, so the next solve does
        /// not see the press again.
        /// </summary>
        internal void ReleaseButtons(UiRegistrar registrar)
        {
            var pressed = registrar.Buttons.Where(name => _ui.ContainsKey(name)).ToList();
            if (pressed.Count == 0) return;

            foreach (var name in pressed) _ui.Remove(name);
        }
    }
}
