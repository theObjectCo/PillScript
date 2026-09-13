using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using PillScript.Icons;

namespace PillScript.Components
{
    /// <summary>
    /// The component's icon. The pill says only that the component is a script, so a name from
    /// the Phosphor set can be given instead. The same drawing appears on the canvas and at the
    /// head of the component's section in the panel, which is what tells several sections apart.
    /// </summary>
    public partial class PillScriptComponent
    {
        static PillScriptComponent()
        {
            // A fetch completes on a background thread, which may not touch the canvas, and
            // Grasshopper caches the icon it already asked for. Both the components using this
            // name and that cache are invalidated here, on the UI thread.
            PhosphorIcons.Arrived += name => Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                foreach (var component in Wearing(name)) component.Redrawn();

                AnnouncePublished();
                Instances.RedrawCanvas();
            }));
        }

        string _iconName;
        Bitmap _drawn;

        /// <summary>The Phosphor name set on this component, or null for the pill.</summary>
        internal string IconName => _iconName;

        /// <summary>The SVG source, for the panel, which renders it instead of using the bitmap.</summary>
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
        /// The canvas caches the last icon it was given, so a change has to be cleared twice:
        /// the local bitmap here and Grasshopper's own cache.
        /// </summary>
        void Redrawn()
        {
            _drawn = null;

            DestroyIconCache();
            OnDisplayExpired(true);
        }

        /// <summary>
        /// Drawn in an ink dark enough to read on the component's grey. Null while the icon is
        /// still being fetched, or if the catalogue has no such name, and the pill is used then.
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
        /// A text box in the menu itself. The catalogue holds some nine thousand names and none
        /// of them are known here, so a name that does not exist leaves the pill in place and stays
        /// in the box, where a typo can be corrected.
        ///
        /// The name is taken on Enter, not on every keystroke, because each new name is a fetch.
        /// The menu is not locked while the box has focus: locking it adds Grasshopper's own Commit
        /// and Cancel items, and Commit never reached this handler.
        /// </summary>
        void AppendIconItem(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "Phosphor icon").ToolTipText =
                "A name from phosphoricons.com, for example gear-six, flask or waves-bold. "
                + "Empty for the pill.";

            var box = Menu_AppendTextItem(menu, _iconName ?? string.Empty, (sender, key) =>
            {
                if (key.KeyCode != Keys.Return && key.KeyCode != Keys.Enter) return;

                SetIconName(sender.Text);
                menu.Close();
            }, null, false);

            box.ToolTipText = "Type a name and press Enter.";
        }
    }
}
