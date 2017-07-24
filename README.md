Ryder
=====

Ryder is a .NET Core library providing the ability to redirect method calls from
one method to another. By extension, it can also redirect property accesses, and event
subscriptions / raises.

[![NuGet](https://img.shields.io/nuget/v/Ryder.svg)](https://nuget.org/packages/Ryder)
[![Latest release](https://img.shields.io/github/release/6A/Ryder.svg)](../../releases/latest)
[![Issues](https://img.shields.io/github/issues-raw/6A/Ryder.svg)](../../issues)
[![License](https://img.shields.io/github/license/6A/Ryder.svg)](./LICENSE.md)

## Get started
#### Redirect a method
```csharp
public static int Incremented(int nbr) => nbr + 1;
public static int Decremented(int nbr) => nbr - 1;

MethodRedirection r = Redirection.Redirect(() => Incremented(0), () => Decremented(1));

Console.WriteLine(Incremented(1)); // => 0.

// Note: you can also invoke the original method:
int incremented = (int)r.InvokeOriginal(null, 1); // => 2.
```

#### Redirect a property
```csharp
public static DateTime PatchedNow => DateTime.FromBinary(0);

PropertyRedirection r = Redirection.Redirect(() => DateTime.Now, () => PatchedNow);

Console.WriteLine(DateTime.Now.ToBinary()); // => 0.

// Note: you can also access the original property:
DateTime now = ((DateTime)r.GetOriginal(null)).ToBinary(); // => A very large number.
```

#### Other features
##### Any `Redirection` also defines the following members:
- `bool IsRedirecting { get; set; }`
- `void Start()`
- `void Stop()`

##### Redirections can be created in multiple ways:
- `MethodRedirection`: `Redirect(Expression<..>, Expression<..>)`, `Redirect(Delegate, Delegate)`, `Redirect(MethodBase, MethodBase)`.
- `PropertyRedirection`: `Redirect(Expression<..>, Expression<..>)`, `Redirect(PropertyInfo, PropertyInfo)`.
- `EventRedirection`: `Redirect(Expression<..>, Expression<..>)`, `Redirect(EventInfo, EventInfo)`.

##### Gloriously unsafe:
By default, Ryder makes many runtime checks when you create a new `Redirection` ([see by yourself](./Ryder/Redirection.cs)). Should you decide to do some *very* experimental and unsafe stuff, you can disable all those checks by setting the static property `Redirection.SkipChecks` to `true`.

## Installation
You can install Ryder through the NuGet package manager:
```powershell
Install-Package Ryder
```

Alternatively, if you really want the barebones version, you can copy-paste
[`Ryder.Lightweight.cs`](./Ryder.Lightweight/Ryder.Lightweight.cs) in your project.

## Additional notes
- Make sure the method you want to redirect does not get inlined by the JIT; if it is inlined,
  redirecting it will most likely break stuff in unexpected ways (for example, the method will
  be correctly redirected, but the next method to be jitted will be incorrect).
- In order to keep the GC from collecting jitted methods, Ryder keeps static references to them.
  Those references are only deleted when `Redirection.Dispose()` is called, after which the `Redirection`
  is no longer guaranteed to work.

## Inspiration
Ryder is highly inspired by [Harmony](https://github.com/pardeike/Harmony), but tries
to take a very minimal approach to redirection, instead of providing the ability to patch individual
instructions. Moreover, it was made with .NET Core in mind.