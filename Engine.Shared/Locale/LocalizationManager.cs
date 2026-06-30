using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Engine.Shared.Assets;
using Engine.Shared.IoC;
using Linguini.Bundle;
using Linguini.Bundle.Builder;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;

namespace Engine.Shared.Locale;

public interface ILocalizationManager
{
    public List<CultureInfo> GetAvailableCultures();

    /// <summary>
    /// Get a string using the fluent key from the current culture.
    /// </summary>
    /// <param name="key">The key name to get from the fluent bundle.</param>
    /// <returns>The current culture key value or fallback culture if it does not exist in current.
    /// Will return the key if it does not exist in the fallback</returns>
    public string GetString(string key);

    /// <summary>
    /// Get a string using the fluent key from the current culture.
    /// </summary>
    /// <param name="key">The key name to get from the fluent bundle.</param>
    /// <param name="args">Collection of variables in the key (the ones from <code>foo = "foo ({$foo})"</code>)</param>
    /// <returns>The current culture key value or fallback culture if it does not exist in current.
    /// Will return the key if it does not exist in the fallback</returns>
    public string GetString(string key, params (string, object)[] args);

    /// <summary>
    /// Set the game culture, this will also change the dotnet culture
    /// </summary>
    public void SetCulture(CultureInfo culture);

    /// <summary>
    /// Set the fallback culture, will be used if a key does not exist in the current culture
    /// </summary>
    public void SetFallbackCulture(CultureInfo culture);

    /// <summary>
    /// Add a culture to the culture list so you can have access to it before the culture loading.
    /// </summary>
    public CultureInfo AddCulture(CultureInfo culture);

    /// <summary>
    /// Register a global function that will be give to all cultures during culture loading.
    /// </summary>
    public void AddFunction(string name, LocFunction.FluentMethod func);

    /// <summary>
    /// Register a function that will be give to given culture during culture loading.
    /// </summary>
    public void AddFunction(CultureInfo culture, string name, LocFunction.FluentMethod func);

    /// <summary>
    /// Load all .ftl files into memory, build the fluent bundle and make all cultures be ready to use.
    /// </summary>
    public void LoadCulture();
    public void ReloadCulture();
    public event Action? OnReloadCulture;
}

[RegisterIoC]
internal sealed class LocalizationManager : ILocalizationManager
{
    public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;
    public CultureInfo? FallbackCulture { get; private set; }
    public ResPath ResPath { get; private set; } = new ("Locale");

    private Dictionary<CultureInfo, FluentBundle> _bundles = new();
    private readonly Dictionary<CultureInfo, List<(string name, ExternalFunction func)>> _functions = new();
    private readonly Dictionary<string, ExternalFunction> _globalFunctions = new();
    public Dictionary<string, CultureInfo> Cultures = new();

    public event Action? OnReloadCulture;

    public LocalizationManager()
    {
    }

    public void LoadCulture()
    {
        var cultureFiles = new Dictionary<CultureInfo, List<string>>();

        foreach (var localeRoot in ResPath.GetFolders())
        {
            var cultures = Directory.GetDirectories(localeRoot);

            foreach (var cultureDir in cultures)
            {
                var name = Path.GetFileName(cultureDir);

                if (!Cultures.TryGetValue(name, out var culture))
                {
                    culture = new CultureInfo(name);
                    Cultures.Add(name, culture);
                }

                if (!cultureFiles.TryGetValue(culture, out var files))
                {
                    files = [];
                    cultureFiles[culture] = files;
                }

                files.AddRange(
                    Directory.GetFiles(
                        cultureDir,
                        "*.ftl",
                        SearchOption.AllDirectories
                    )
                );
            }
        }

        foreach (var (culture, files) in cultureFiles)
            LoadBundle(culture, files);
    }

    public void ReloadCulture()
    {
        _bundles.Clear();
        Cultures.Clear();

        LoadCulture();
        OnReloadCulture?.Invoke();
    }

    public List<CultureInfo> GetAvailableCultures()
    {
        return Cultures.Values.ToList();
    }

    public CultureInfo AddCulture(CultureInfo culture)
    {
        Cultures.TryAdd(culture.Name, culture);
        return Cultures[culture.Name];
    }

    public void SetCulture(CultureInfo culture)
    {
        Culture = culture;
        Cultures.TryAdd(culture.Name, culture);
        CultureInfo.CurrentCulture = culture;
    }

    public void SetFallbackCulture(CultureInfo culture)
    {
        FallbackCulture = culture;
        Cultures.TryAdd(culture.Name, culture);
    }

    public void AddFunction(CultureInfo culture, string name, LocFunction.FluentMethod func)
    {
        if (!_functions.TryGetValue(culture, out var list))
        {
            list = new();
            _functions[culture] = list;
        }

        list.Add((name, LocFunction.Wrap(func)));
    }

    public void AddFunction(string name, LocFunction.FluentMethod func)
    {
        _globalFunctions[name] = LocFunction.Wrap(func);
    }

    public string GetString(string key)
    {
        return GetString(key, []); // get from the function below
    }

    public string GetString(string key, params (string, object)[] args)
    {
        var bundle = _bundles[Culture];
        var str = GetStringByBundle(key, bundle, args);
        if (str != key)
            return str;

        else if (FallbackCulture is not null) // use fallback
        {
            var fallback = _bundles[FallbackCulture];
            str = GetStringByBundle(key, fallback, args); // return the key if null
        }
        
        return str;
    }

    public string GetStringByBundle(string key, FluentBundle bundle, params (string, object)[] args)
    {
        var fluentArgs = new Dictionary<string, IFluentType>();

        for (int i = 0; i < args.Length; i++)
        {
            var (k, v) = args[i];
            fluentArgs[k] = FluentFromObject(v);
        }

        if (!bundle.TryGetAttrMessage(key, fluentArgs, out var errors, out var message))
        {
            if (errors is not null)
                foreach (var error in errors)
                    Log.Error(error.ToString());
            
            return key;
        }
        return message;
        
    }

    private void LoadBundle(CultureInfo loc, IEnumerable<string> files)
    {
        var builder = LinguiniBuilder
        .Builder()
        .CultureInfo(loc);

        foreach (var file in files)
        {
            //Log.Debug($"Reading loc file from {file}");
            builder.AddFile(file);
        }
        var builderReady = builder.SkipResources();
        foreach (var (name, func) in _globalFunctions)
                builderReady.AddFunction(name, func);

        if (_functions.TryGetValue(loc, out var funcs))
            foreach (var (name, func) in funcs)
                builderReady.AddFunction(name, func);

        var (bundle, errors) = builderReady.Build();
        if (errors is not null && errors.Count > 0) 
        {
            Log.Error($"Error(s) in locale building for {loc.Name}:");
            foreach (var error in errors)
                Log.Error("LOC ERROR " + error.ToString());
        }

        _bundles.Add(loc, bundle);
    }

    // this fluent is based on old versions of fluent.js and it is so fucked, i made this to convert objects to fluent types
    // cool-fact: theres no other alive port of project fluent to c# <3
    public IFluentType FluentFromObject(object obj)
    {
        return obj switch
        {
            // Engine wrappers
            EntityUid uid => (FluentNumber)uid.Id,
            ProtoId proto => (FluentString)proto.Value,

            // .net warppers
            null => (FluentString)"",
            string str => (FluentString)str,
            bool b => (FluentString)b.ToString().ToLowerInvariant(),
            Enum e => (FluentString)e.ToString().ToLowerInvariant(),

            byte num => (FluentNumber)num,
            sbyte num => (FluentNumber)num,
            short num => (FluentNumber)num,
            ushort num => (FluentNumber)num,
            int num => (FluentNumber)num,
            uint num => (FluentNumber)num,
            long num => (FluentNumber)num,
            ulong num => (FluentNumber)num,
            float num => (FluentNumber)num,
            double num => (FluentNumber)num,

            _ => (FluentString)obj.ToString()!
        };
    }
}
