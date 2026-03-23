using Evergine.Bindings.OpenXR;
using System.Diagnostics.CodeAnalysis;

namespace KSA.XR
{
	public static class XrQuaternionfExtensions
	{
		public static string ToString(this XrQuaternionf quaternion)
		{
			return $"({quaternion.x}, {quaternion.y}, {quaternion.z}, {quaternion.w})";
		}

		public static string ToString(this XrQuaternionf quaternion, IFormatProvider? provider)
		{
			return $"({quaternion.x.ToString(provider)}, {quaternion.y.ToString(provider)}, {quaternion.z.ToString(provider)}, {quaternion.w.ToString(provider)})";
		}

		public static string ToString(this XrQuaternionf quaternion, [StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format)
		{
			return $"({quaternion.x.ToString(format)}, {quaternion.y.ToString(format)}, {quaternion.z.ToString(format)}, {quaternion.w.ToString(format)})";
		}

		public static string ToString(this XrQuaternionf quaternion, IFormatProvider? provider, [StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format)
		{
			return $"({quaternion.x.ToString(format, provider)}, {quaternion.y.ToString(format, provider)}, {quaternion.z.ToString(format, provider)}, {quaternion.w.ToString(format, provider)})";
		}
	}
}
