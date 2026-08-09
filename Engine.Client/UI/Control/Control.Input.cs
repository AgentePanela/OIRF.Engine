using System;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// The basic node in the GUI system.
/// </summary>
public partial class Control : IDisposable
{
    private bool _canKeyboardFocus;
}