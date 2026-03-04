using System;
using UnityEngine;

namespace Arterra.Utils
{
    /// <summary>
    /// Put this on an Option&lt;List&lt;TElement&gt;&gt; field to display the list with per-element labels
    /// derived from a nested SerializeReference field on each element (default: "value").
    ///
    /// Example:
    /// [TypeNameElementList("value")]
    /// public Option&lt;List&lt;ReferenceOption&lt;IBehaviorSetting&gt;&gt;&gt; Settings;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TypeNameElementListAttribute : PropertyAttribute
    {
        public readonly string elementValuePath;
        public readonly Type[] allowedTypes;

        /// <param name="elementValuePath">
        /// Relative path from the array element to the SerializeReference field used for naming.
        /// For ReferenceOption&lt;IBehaviorSetting&gt; this is "value".
        /// </param>
        /// <param name="allowedTypes">
        /// Array of types that can be added to the list. If provided, clicking + will show a popup to select from these types.
        /// </param>
        public TypeNameElementListAttribute(string elementValuePath = "value", params Type[] allowedTypes)
        {
            this.elementValuePath = elementValuePath;
            this.allowedTypes = allowedTypes;
        }
    }
}
