// Sample shown when the page is opened outside Rhino, so the editor can be styled and checked
// in a browser. The component never takes this path.

var SAMPLE = [
  'public class Script : ScriptBase',
  '{',
  '    public void RunScript(',
  '        [Description("Corner points of the outline")] List<Point3d> points,',
  '        [Description("Extra points per segment")] [Default(2)] int divisions,',
  '        out Polyline outline,',
  '        out List<Point3d> samples,',
  '        out double length)',
  '    {',
  '        // A verbatim string and an escape, to check how both are coloured.',
  '        var label = @"segment ""0""";',
  '        var unused = 0;',
  '',
  '        outline = PolyHelper.Close(points);',
  '        samples = PolyHelper.Divide(outline, divisions);',
  '        length = outline.Length;',
  '',
  '        foreach (var point in samples)',
  '        {',
  '            if (point.Z > 1e-6) Warning(label + " is off the plane");',
  '        }',
  '',
  '        Print("{0} corners in, {1} samples out", points.Count, samples.Count);',
  '    }',
  '}'
].join('\n');

var SAMPLE_PROJECT = [
  '<Project Sdk="Microsoft.NET.Sdk">',
  '  <PropertyGroup>',
  '    <TargetFramework>net7.0-windows</TargetFramework>',
  '  </PropertyGroup>',
  '</Project>'
].join('\n');

var SAMPLE_HELPER = [
  'static class PolyHelper',
  '{',
  '    public static Polyline Close(List<Point3d> points)',
  '    {',
  '        var polyline = new Polyline(points);',
  '        if (polyline.Count > 2 && !polyline.IsClosed) polyline.Add(polyline[0]);',
  '',
  '        return polyline;',
  '    }',
  '}'
].join('\n');
