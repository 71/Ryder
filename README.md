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

Incremented(1); // => 2.

MethodRedirection r = Redirection.Redirect<Func<int, int>>(Incremented, Decremented);

Incremented(1); // => 0.

// You can also invoke the original method:
r.InvokeOriginal(null, 1); // => 2.

// You can also stop the redirection...
r.Stop(); // or r.IsRedirecting = false, or r.Dispose().
Incremented(1); // => 2.

// ... and restart it
r.Start(); // or r.IsRedirecting = true, unless you disposed it, in which case it's no longer usable
Incremented(1); // => 0.
```

#### Using Reactive Extensions
```csharp
MethodInfo method = typeof(DateTime)
    .GetProperty(nameof(DateTime.Now), BindingFlags.Static | BindingFlags.Public)
    .GetGetMethod();

int count = 0;
DateTime bday = new DateTime(1955, 10, 28);

using (Redirection.Observe(method)
                  .Where(_ => count++ % 2 == 0)
                  .Subscribe(ctx => ctx.ReturnValue = bday))
{
    DateTime.Now.ShouldBe(bday);
    DateTime.Now.ShouldNotBe(bday);
    DateTime.Now.ShouldBe(bday);
    DateTime.Now.ShouldNotBe(bday);
}

DateTime.Now.ShouldNotBe(bday);
DateTime.Now.ShouldNotBe(bday);
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

##### Tests
All features are tested in [Ryder.Tests](./Ryder.Tests). Please check it out, as it contains some real-world-usage code.

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