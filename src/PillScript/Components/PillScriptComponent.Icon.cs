using System;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using PillScript.Icons;

namespace PillScript.Components
{
    /// <summary>
    /// The icon a component wears. A script does more than one thing over its life and the pill
    /// says only that it is a script, so a component may be given any name out of the Phosphor
    /// set instead. The same drawing goes on the canvas and at the head of its section in the
    /// panel, which is what makes a panel of four scripts readable at a glance.
    /// </summary>
    public partial class PillScriptComponent
    {
        static PillScriptComponent()
        {
            // A fetch lands on a thread that may do nothing to the canvas, and the icon Grasshopper
            // already asked for is cached, so the components wearing this name are told to forget.
            PhosphorIcons.Arrived += name => Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                foreach (var component in Wearing(name)) component.Redrawn();

                AnnouncePublished();
                Instances.RedrawCanvas();
            }));
        }

        string _iconName;
        Bitmap _drawn;

        /// <summary>The Phosphor name this component wears, or null for the pill.</summary>
        internal string IconName => _iconName;

        /// <summary>The drawing itself, for the panel, which shows it as it is rather than drawn.</summary>
        internal string IconSvg => _iconName == null ? null : PhosphorIcons.Svg(_iconName);

        protected override Bitmap Icon => Drawn() ?? ComponentIcon.Bitmap;

        internal void SetIconName(string name)
        {
            var clean = PhosphorIcons.Clean(name);
            if (clean == _iconName) return;

            RecordUndoEvent("Phosphor icon");

            _iconName = clean;
            Redrawn();

            AnnouncePublished();
            Instances.RedrawCanvas();
        }

        /// <summary>
        /// The canvas keeps the last icon it was given, so changing one means saying so twice:
        /// once here and once to Grasshopper.
        /// </summary>
        void Redrawn()
        {
            _drawn = null;

            DestroyIconCache();
            OnDisplayExpired(true);
        }

        /// <summary>
        /// Ink dark enough to read on the component's own grey. Null while the icon is being
        /// fetched or if the catalogue has no such name, and then the pill stands in.
        /// </summary>
        Bitmap Drawn()
        {
            if (_iconName == null) return null;
            if (_drawn != null) return _drawn;

            var svg = PhosphorIcons.Svg(_iconName);
            if (svg == null) return null;

            return _drawn = SvgRaster.Raster(svg, 24, Color.FromArgb(255, 40, 40, 40));
        }

        static System.Collections.Generic.IEnumerable<PillScriptComponent> Wearing(string name)
        {
            var server = Instances.DocumentServer;
            if (server == null) yield break;

            foreach (GH_Document document in server)
            {
                foreach (var obj in document.Objects)
                {
                    if (obj is PillScriptComponent component &&
                        string.Equals(component.IconName, name, StringComparison.OrdinalIgnoreCase))
                        yield return component;
                }
            }
        }

        /// <summary>
        /// Asks for a name and takes whatever is typed. Nothing here knows which names exist, and
        /// the set has nine thousand of them: one that turns out not to exist leaves the pill and
        /// stays written down, so a typo is fixed by editing it rather than by guessing again.
        /// </summary>
        void AskForIcon()
        {
            var asked = Rhino.UI.Dialogs.ShowEditBox(
                "PillScript",
                "Icon name from phosphoricons.com, for example gear-six, flask or waves-bold. "
                + "Leave it empty for the pill.",
                _iconName ?? string.Empty,
                false,
                out var typed);

            if (asked) SetIconName(typed);
        }
    }
}
