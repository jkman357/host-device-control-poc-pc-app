// Copyright © 2026 Ray Yang. All rights reserved.
// No license is granted. See LICENSE and NOTICE.md.

using System;
using System.Text;

namespace HostDeviceControl.Core.Diagnostics;

/// <summary>
/// Normalizes externally influenced diagnostic text before it is presented or
/// written to a line-oriented log.
/// </summary>
public static class DiagnosticText
{
    /// <summary>
    /// Maximum number of characters retained in one diagnostic message.
    /// </summary>
    public const int MaximumLength = 512;

    /// <summary>
    /// Replaces line breaks and control characters so one input value cannot
    /// inject additional log entries or disrupt the user interface.
    /// </summary>
    /// <param name="value">Potentially untrusted text.</param>
    /// <returns>A single-line bounded diagnostic value.</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        int maximumInputLength = Math.Min(value.Length, MaximumLength);
        var builder = new StringBuilder(maximumInputLength);

        for (int index = 0; index < maximumInputLength; index++)
        {
            char character = value[index];

            if ((character == '\r') || (character == '\n') || (character == '\t'))
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
