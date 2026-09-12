using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace PillScript.Scripting
{
    /// <summary>
    /// Translates the CLR types found on a RunScript signature into Grasshopper parameters.
    /// </summary>
    internal static class ParamFactory
    {
        static readonly Dictionary<Type, Func<IGH_Param>> Map = new Dictionary<Type, Func<IGH_Param>>
        {
            [typeof(bool)] = () => new Param_Boolean(),
            [typeof(int)] = () => new Param_Integer(),
            [typeof(long)] = () => new Param_Integer(),
            [typeof(double)] = () => new Param_Number(),
            [typeof(float)] = () => new Param_Number(),
            [typeof(decimal)] = () => new Param_Number(),
            [typeof(string)] = () => new Param_String(),
            [typeof(Guid)] = () => new Param_Guid(),
            [typeof(DateTime)] = () => new Param_Time(),
            [typeof(Color)] = () => new Param_Colour(),
            [typeof(System.Numerics.Complex)] = () => new Param_Complex(),
            [typeof(Point3d)] = () => new Param_Point(),
            [typeof(Point3f)] = () => new Param_Point(),
            [typeof(Vector3d)] = () => new Param_Vector(),
            [typeof(Plane)] = () => new Param_Plane(),
            [typeof(Line)] = () => new Param_Line(),
            [typeof(Circle)] = () => new Param_Circle(),
            [typeof(Arc)] = () => new Param_Arc(),
            [typeof(Interval)] = () => new Param_Interval(),
            [typeof(UVInterval)] = () => new Param_Interval2D(),
            [typeof(Box)] = () => new Param_Box(),
            [typeof(Transform)] = () => new Param_Transform(),
            [typeof(Curve)] = () => new Param_Curve(),
            [typeof(NurbsCurve)] = () => new Param_Curve(),
            [typeof(PolyCurve)] = () => new Param_Curve(),
            [typeof(Polyline)] = () => new Param_Curve(),
            [typeof(Surface)] = () => new Param_Surface(),
            [typeof(NurbsSurface)] = () => new Param_Surface(),
            [typeof(Brep)] = () => new Param_Brep(),
            [typeof(SubD)] = () => new Param_SubD(),
            [typeof(Mesh)] = () => new Param_Mesh(),
            [typeof(GeometryBase)] = () => new Param_Geometry(),
        };

        /// <summary>Creates the parameter that carries <paramref name="type"/> across a wire.</summary>
        public static IGH_Param Create(Type type)
        {
            if (Map.TryGetValue(type, out var factory)) return factory();

            // A goo type declared directly, e.g. GH_Number, maps onto the parameter that holds it.
            if (typeof(IGH_Goo).IsAssignableFrom(type))
            {
                var param = GooParam(type);
                if (param != null) return param;
            }

            if (typeof(GeometryBase).IsAssignableFrom(type)) return new Param_Geometry();
            return new Param_GenericObject();
        }

        static IGH_Param GooParam(Type gooType)
        {
            if (gooType == typeof(GH_Boolean)) return new Param_Boolean();
            if (gooType == typeof(GH_Integer)) return new Param_Integer();
            if (gooType == typeof(GH_Number)) return new Param_Number();
            if (gooType == typeof(GH_String)) return new Param_String();
            if (gooType == typeof(GH_Point)) return new Param_Point();
            if (gooType == typeof(GH_Vector)) return new Param_Vector();
            if (gooType == typeof(GH_Plane)) return new Param_Plane();
            if (gooType == typeof(GH_Line)) return new Param_Line();
            if (gooType == typeof(GH_Circle)) return new Param_Circle();
            if (gooType == typeof(GH_Arc)) return new Param_Arc();
            if (gooType == typeof(GH_Curve)) return new Param_Curve();
            if (gooType == typeof(GH_Surface)) return new Param_Surface();
            if (gooType == typeof(GH_Brep)) return new Param_Brep();
            if (gooType == typeof(GH_Mesh)) return new Param_Mesh();
            if (gooType == typeof(GH_SubD)) return new Param_SubD();
            if (gooType == typeof(GH_Box)) return new Param_Box();
            if (gooType == typeof(GH_Colour)) return new Param_Colour();
            if (gooType == typeof(GH_Interval)) return new Param_Interval();
            if (gooType == typeof(GH_Transform)) return new Param_Transform();
            return null;
        }

        /// <summary>
        /// Stores a compile time default on the parameter so an unwired socket still has a value.
        /// Silently does nothing when the value cannot be held by that parameter kind.
        /// </summary>
        public static void ApplyDefault(IGH_Param param, object value)
        {
            if (value == null) return;
            try
            {
                switch (param)
                {
                    case Param_Boolean p: p.PersistentData.Append(new GH_Boolean(Convert.ToBoolean(value))); break;
                    case Param_Integer p: p.PersistentData.Append(new GH_Integer(Convert.ToInt32(value))); break;
                    case Param_Number p: p.PersistentData.Append(new GH_Number(Convert.ToDouble(value))); break;
                    case Param_String p: p.PersistentData.Append(new GH_String(Convert.ToString(value))); break;
                }
            }
            catch (Exception)
            {
                // A default that does not fit the parameter is not worth failing a compile over.
            }
        }
    }
}
