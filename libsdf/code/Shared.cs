using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Sdf
{
    /// <summary>
    /// Quality settings for <see cref="Sdf2DLayer"/>.
    /// </summary>
    public enum WorldQuality
    {
        /// <summary>
        /// Cheap and cheerful, suitable for frequent (per-frame) edits.
        /// </summary>
        Low,

        /// <summary>
        /// Recommended quality for most cases.
        /// </summary>
        Medium,

        /// <summary>
        /// More expensive to update and network, but a much smoother result.
        /// </summary>
        High,

        /// <summary>
        /// Only use this for small, detailed objects!
        /// </summary>
        Extreme,

        /// <summary>
        /// Manually tweak quality parameters.
        /// </summary>
        Custom = -1
    }
}
