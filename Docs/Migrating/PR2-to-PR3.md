## Migrating from pre-release 0.2.0 to pre-release 0.3.0
A bit of csproj changes has happend.

### 1. Add new contentless nuget package
Install it by using:
```bash
dotnet add package Contentless --version 4.2.2
```

and add this in your game client csproj:
```xml
<ItemGroup>
    <MonoGameContentReference Include="Content\Content.mgcb" />
</ItemGroup>
```

after this, in your Content\ folder add a `Contentless.json` file and insert this:
```json
{
    "exclude": [
        "obj/*",
        "bin/*"
    ],
    "logSkipped": false
}
```

### 2. Adding engine shaders syslinks
Inside your game Content folder, open your OS terminal.

**On Windows:**
If your game is using git, please make sure `git config --get core.symlinks` is set to `true`.

> Open CMD as administrator in Windows to this works.
```bash
mklink /D EngineShaders ..\..\Engine\Engine.Client\EngineShaders
```

**On Linux/macOS:**
```bash
ln -s ../../Engine/Engine.Client/EngineShaders EngineShaders
```

### 3. Clear Content.mgcb content

Open Content.mgcb and clear everything below

```
#---------------------------------- Content ---------------------------------#

[...]
```