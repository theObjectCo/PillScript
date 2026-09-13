using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PillScript.Scripting
{
    /// <summary>
    /// Reports a script failure in the author's own terms: the exception that was thrown, and the
    /// line of the script it came from.
    /// </summary>
    internal static class ScriptFault
    {
        /// <summary>
        /// Calling a script through reflection wraps whatever it threw. The wrapper carries
        /// nothing the author can act on, so it is unwrapped.
        /// </summary>
        public static Exception Unwrap(Exception exception)
            => exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;

        /// <summary>
        /// Names the exception and, when the script's own frames are in the trace, the line it
        /// happened on. Scripts are compiled with debug information, so the frames are usually
        /// there.
        /// </summary>
        public static string Describe(Exception exception)
        {
            var message = exception.GetType().Name + ": " + exception.Message;

            var frame = new StackTrace(exception, true).GetFrames()?
                .FirstOrDefault(f => f.GetFileLineNumber() > 0);

            if (frame == null) return message;

            var file = Path.GetFileName(frame.GetFileName());
            return message + " (" + file + ", line " + frame.GetFileLineNumber() + ")";
        }
    }
}
