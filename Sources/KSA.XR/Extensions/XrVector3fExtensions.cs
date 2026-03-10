using Evergine.Bindings.OpenXR;
using System.Diagnostics.CodeAnalysis;

namespace KSA.XR
{
	public static class XrVector3fExtensions
	{
		public static string ToString(this XrVector3f vector)
		{
			return $"({vector.x}, {vector.y}, {vector.z})";
		}

		public static string ToString(this XrVector3f vector, IFormatProvider? provider)
		{
			return $"({vector.x.ToString(provider)}, {vector.y.ToString(provider)}, {vector.z.ToString(provider)})";
		}

		public static string ToString(this XrVector3f vector, [StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format)
		{
			return $"({vector.x.ToString(format)}, {vector.y.ToString(format)}, {vector.z.ToString(format)})";
		}

		public static string ToString(this XrVector3f vector, IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format)
		{
			return $"({vector.x.ToString(format, provider)}, {vector.y.ToString(format, provider)}, {vector.z.ToString(format, provider)})";
		}
	}
}
