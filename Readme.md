# Uno.ImmutableUI

This project is an experiment based on the work from @praeclarum: https://github.com/praeclarum/ImmutableUI to experiment 
on Immutable UI objects creation. 

This implementation targets vanilla UWP, uses Roslyn through [Uno.SourgeGenerator](https://github.com/nventive/Uno.SourceGeneration) and 
[Uno.CodeGen](https://github.com/nventive/Uno.CodeGen/blob/master/doc/Immutable%20Generation.md) for immutable types management, and equality management.

The effort tries to determine the advantages and pitfalls of the large amount of available properties in the `Windows.UI` namespace, particularly during 
change tracking.

There's an attempt at instance equality through a `ConditionaWeakTable`, though it only works properly
for 1..1 updates (e.g. ContentControl.Content) where are for collections there's a need for a diffing 
algorithm that is not provided here.

Here's the way to use it:

```csharp
private int _counter;

public MainPage()
{
    this.InitializeComponent();

    Content = Build().Create();
}

void SetState() => Build().Apply(Content);

DelegateCommand _push => new DelegateCommand(() => {
    _counter++;
    SetState();
});

StackPanelModel Build() =>
            
    new StackPanelModel.Builder
    {
        Padding = new ThicknessModel(42),
        Children = ImmutableArray.Create<UIElementModel>(
            new TextBlockModel.Builder { Text = _counter.ToString(), FontSize = 42 }.ToImmutable(),
            new ButtonModel.Builder { Content = "Increment", FontSize = 42, Command = _push }.ToImmutable()
        )
    };
```