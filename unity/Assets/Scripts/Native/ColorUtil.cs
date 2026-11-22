using System;
using UnityEngine;

/// <summary>
/// Colours the way the game has always computed them (Three.js with colour management on): hex numbers are sRGB and are kept
/// as linear values, which is what the Cozy shaders are handed (unity/NATIVE_PLAN.md 2.3). Unity's own Color from hex would be
/// sRGB, so material and vertex colours go through these.
/// </summary>
public static class ColorUtil
{
    public static double SRGBToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);

    public static double LinearToSRGB(double c) => c < 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 0.41666) - 0.055;

    /// <summary>A hex colour (0xrrggbb, sRGB) as linear r, g, b and the given alpha.</summary>
    public static Color Linear(int hex, float alpha = 1)
    {
        return new Color(
            (float)SRGBToLinear(((hex >> 16) & 255) / 255.0),
            (float)SRGBToLinear(((hex >> 8) & 255) / 255.0),
            (float)SRGBToLinear((hex & 255) / 255.0),
            alpha);
    }

    /// <summary>A linear colour as sRGB, for what expects gamma values (UI, Light.color, which Unity linearises itself).</summary>
    public static Color ToSRGB(Color linear) =>
        new Color((float)LinearToSRGB(linear.r), (float)LinearToSRGB(linear.g), (float)LinearToSRGB(linear.b), linear.a);

    /// <summary>A linear colour as a shader vector (rgb, alpha in w).</summary>
    public static Vector4 Vec(Color linear) => new Vector4(linear.r, linear.g, linear.b, linear.a);
}
