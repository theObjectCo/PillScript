using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace PillScript
{
    public class PillScriptInfo : GH_AssemblyInfo
    {
        public override string Name => "PillScript";

        public override Bitmap Icon => Components.ComponentIcon.Bitmap;

        public override string Description =>
            "A C# script component with a Monaco editor, explicit compilation and a project on " +
            "disk that an IDE can open.";

        public override Guid Id => new Guid("2B6E9D44-31A7-4C2F-8E77-5A0C9B3F1D22");

        public override string AuthorName => string.Empty;

        public override string AuthorContact => string.Empty;
    }
}
