using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Uno.RoslynHelpers;
using Uno.SourceGeneration;

namespace Uno.ImmutableUI.Generator
{
	[GenerateBefore("Uno.TemporaryImmutableGenerator")]
	[GenerateBefore("Uno.EqualityGenerator")]
	public class ImmutableUIGenerator : SourceGenerator
	{
		private ConcurrentBag<INamedTypeSymbol> _queue = new ConcurrentBag<INamedTypeSymbol>();
		private HashSet<INamedTypeSymbol> _processed = new HashSet<INamedTypeSymbol>();
		private INamedTypeSymbol _objectSymbol;
		private INamedTypeSymbol _iListSymbol;
		private INamedTypeSymbol _uiElementSymbol;
		private INamedTypeSymbol _dependencyObject;

		public override void Execute(SourceGeneratorContext context)
		{
			// Debugger.Launch();

			var project = context.GetProjectInstance();

			_objectSymbol = context.Compilation.GetTypeByMetadataName("System.Object");
			_iListSymbol = context.Compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1");
			_uiElementSymbol = context.Compilation.GetTypeByMetadataName("Windows.UI.Xaml.UIElement");
			_dependencyObject = context.Compilation.GetTypeByMetadataName("Windows.UI.Xaml.DependencyObject");

			QueueType(context, "Windows.UI.Xaml.Controls.TextBlock");
			QueueType(context, "Windows.UI.Xaml.Controls.Panel");
			QueueType(context, "Windows.UI.Xaml.Controls.StackPanel");
			QueueType(context, "Windows.UI.Xaml.Controls.Button");

			while (_queue.Any())
			{
				if (_queue.TryTake(out var symbol))
				{
					BuildType(context, symbol);
				}
			}
		}

		private void QueueType(SourceGeneratorContext context, string FullyQualifiedMetadataName)
		{
			var item = context.Compilation.GetTypeByMetadataName(FullyQualifiedMetadataName);

			if (item != null)
			{
				_queue.Add(item);
			}
		}

		private void BuildType(SourceGeneratorContext context, INamedTypeSymbol symbol)
		{
			if (_processed.Contains(symbol))
			{
				return;
			}

			_processed.Add(symbol);

			if (!IsWindowsSymbol(symbol))
			{
				return;
			}

			var builder = new IndentedStringBuilder();

			builder.AppendLineInvariant("using Uno;");
			builder.AppendLineInvariant("using System.Runtime.CompilerServices;");

			using (builder.BlockInvariant($"namespace {Relocate(symbol.ContainingNamespace.ToDisplayString())}"))
			{
				AddTypeToQueue(symbol.BaseType);

				var baseType = symbol.BaseType != _objectSymbol && !symbol.IsValueType ? $": {Relocate(symbol.BaseType)}" : "";

				switch (symbol.TypeKind)
				{
					case TypeKind.Enum:
						GenerateEnum(symbol, builder, baseType);
						break;

					default:
						GenerateClass(symbol, builder, baseType);
						break;
				}
			}

			context.AddCompilationUnit(symbol.Name, builder.ToString());
		}

		private static bool IsWindowsSymbol(ITypeSymbol symbol) => symbol.ContainingNamespace.ToString().StartsWith("Windows");

		private void GenerateEnum(INamedTypeSymbol symbol, IndentedStringBuilder builder, string baseType)
		{
			using (builder.BlockInvariant($"public enum {symbol.Name}Model : {symbol.EnumUnderlyingType.ToDisplayString()}"))
			{
				foreach(var field in symbol.GetFields())
				{
					builder.AppendLineInvariant($"{field.Name} = {field.ConstantValue?.ToString()},");
				}
			}
		}

		private void GenerateClass(INamedTypeSymbol symbol, IndentedStringBuilder builder, string baseType)
		{
			builder.AppendLineInvariant($"[GeneratedImmutableModelAttribute]");

			using (builder.BlockInvariant($"public partial class {symbol.Name}Model {baseType}"))
			{
				GenerateGenericClass(symbol, builder);
			}
		}

		private void GenerateGenericClass(INamedTypeSymbol symbol, IndentedStringBuilder builder)
		{
			var properties =
				from prop in symbol.GetProperties()
				where !prop.IsStatic 
					&& prop.DeclaredAccessibility == Accessibility.Public
					&& (prop.SetMethod != null || FindListElementType(prop.Type as INamedTypeSymbol) != null)
				select prop;

			foreach (var property in properties)
			{
				if (property.Name == "ThemeDictionaries")
				{
					continue;
				}

				if (FindListElementType(property.Type) is INamedTypeSymbol elementType)
				{
					builder.AppendLineInvariant($"public System.Collections.Immutable.IImmutableList<{Relocate(elementType.ToString())}Model> {property.Name} {{{{ get; }}}}");
					AddTypeToQueue(elementType);
				}
				else
				{
					builder.AppendLineInvariant($"public {Relocate(property.Type)} {property.Name} {{{{ get; }}}}");


					if (property.Type is INamedTypeSymbol nts)
					{
						AddTypeToQueue(nts);

						if (nts.TypeParameters.Any())
						{
							foreach (var typeParam in nts.TypeArguments)
							{
								if (typeParam is INamedTypeSymbol nts2)
								{
									_queue.Add(nts2);
								}
							}
						}
					}
				}
			}

			var virtualModifier = symbol.BaseType == _objectSymbol || symbol.IsValueType ? "virtual" : "override";

			using (builder.BlockInvariant($"public {virtualModifier} Windows.UI.Xaml.UIElement CreateUIElement()"))
			{
				if (symbol.Is(_uiElementSymbol))
				{
					builder.AppendLineInvariant($"return Create();");
				}
				else
				{
					builder.AppendLineInvariant($"throw new System.InvalidOperationException(\"Cannot create an UIElement of type {symbol.ToDisplayString()}\");");
				}
			}

			var newModifier = symbol.BaseType == _objectSymbol || symbol.IsValueType ? "" : "new";

			using (builder.BlockInvariant($"public {newModifier} {symbol.ToDisplayString()} Create()"))
			{
				if (!symbol.IsAbstract
					&& symbol.GetMethods().Any(m =>
					m.MethodKind == MethodKind.Constructor
					&& m.DeclaredAccessibility == Accessibility.Public
					&& !m.Parameters.Any())
				)
				{
					builder.AppendLineInvariant($"var instance = new {symbol.ToDisplayString()}();");

					builder.AppendLineInvariant($"Apply(instance);");

					builder.AppendLineInvariant($"return instance;");
				}
				else
				{
					builder.AppendLineInvariant($"throw new System.InvalidOperationException(\"Cannot create an instance of type {symbol.ToDisplayString()}\");");
				}
			}

			builder.AppendLineInvariant($"private static ConditionalWeakTable<object, {symbol.Name}Model> _state = new ConditionalWeakTable<object, {symbol.Name}Model>();");

			builder.AppendLineInvariant($"partial void ApplyPartial({symbol.ToDisplayString()} actual);");

			if (symbol != _uiElementSymbol && symbol != _dependencyObject && symbol.Is(_uiElementSymbol))
			{
				using (builder.BlockInvariant($"public {virtualModifier} void Apply(Windows.UI.Xaml.UIElement actual)"))
				{
						using (builder.BlockInvariant($"if(actual is {symbol.ToDisplayString()} current)"))
						{
							builder.AppendLineInvariant($"Apply(current);");
						}

						if (symbol.BaseType != _objectSymbol && !symbol.IsValueType)
						{
							builder.AppendLineInvariant($"base.Apply(actual);");
						}
				}
			}

			var uiElementVirtual = symbol == _uiElementSymbol ? "virtual" : "";

			using (builder.BlockInvariant($"public {uiElementVirtual} void Apply({symbol.ToDisplayString()} actual)"))
			{
				builder.AppendLineInvariant($"_state.TryGetValue(actual, out var previousModel);");

				using (builder.BlockInvariant($"if(!(previousModel?.Equals(this) ?? false))"))
				{
					builder.AppendLineInvariant($"_state.AddOrUpdate(actual, this);");

					if (symbol.Is(_uiElementSymbol))
					{
						builder.AppendLineInvariant($"base.Apply(actual);");
					}

					foreach (var property in properties)
					{
						using (builder.BlockInvariant($"if(Is{property.Name}Set)"))
						{
							if (FindListElementType(property.Type) is INamedTypeSymbol elementType)
							{
								using (builder.BlockInvariant($"if(actual.{property.Name} != null)"))
								{
									using (builder.BlockInvariant($"if({property.Name} != null)"))
									{
										builder.AppendLineInvariant($"actual.{property.Name}.Clear();");
										using (builder.BlockInvariant($"foreach(var item in {property.Name})"))
										{
											if (elementType.Is(_uiElementSymbol))
											{
												builder.AppendLineInvariant($"actual.{property.Name}.Add(item.CreateUIElement());");
											}
											else
											{
												builder.AppendLineInvariant($"actual.{property.Name}.Add(item.Create());");
											}
										}
									}
									using (builder.BlockInvariant($"else"))
									{
										builder.AppendLineInvariant($"actual.{property.Name}.Clear();");
									}
								}
							}
							else if ((property.Type.TypeKind == TypeKind.Class || property.Type.TypeKind == TypeKind.Struct) && IsWindowsSymbol(property.Type))
							{
								builder.AppendLineInvariant($"var _{property.Name}Value = {property.Name}?.Create();");
								using (builder.BlockInvariant($"if(_{property.Name}Value != null)"))
								{
									if (property.Type.IsValueType)
									{
										builder.AppendLineInvariant($"actual.{property.Name} = _{property.Name}Value.Value;");
									}
									else
									{
										builder.AppendLineInvariant($"actual.{property.Name} = _{property.Name}Value;");
									}
								}
							}
							else if (property.Type.TypeKind == TypeKind.Enum)
							{
								builder.AppendLineInvariant($"actual.{property.Name} = ({property.Type.ToString()})({(property.Type as INamedTypeSymbol).EnumUnderlyingType.ToDisplayString()}){property.Name};");
							}
							else
							{
								if (property.Type.TypeKind == TypeKind.Class)
								{
									builder.AppendLineInvariant($"actual.{property.Name} = {property.Name};");
								}
								else
								{
									builder.AppendLineInvariant($"actual.{property.Name} = {property.Name};");
								}
							}
						}
					}

					builder.AppendLineInvariant($"ApplyPartial(actual);");
				}
			}
		}

		private INamedTypeSymbol FindListElementType(ITypeSymbol symbol)
		{
			if (symbol != null)
			{
				var listType = symbol.GetAllInterfaces().FirstOrDefault(i => i.OriginalDefinition == _iListSymbol);

				if (listType != null)
				{
					return listType.TypeArguments.First() as INamedTypeSymbol;
				}
			}

			return null;
		}

		private void AddTypeToQueue(INamedTypeSymbol nts)
		{
			if (!_queue.Contains(nts))
			{
				_queue.Add(nts);
			}
		}

		private string Relocate(ITypeSymbol type)
		{
			if(type.ContainingNamespace.ToString().StartsWith("Windows"))
			{
				return $"{Relocate(type.ToDisplayString())}Model";
			}
			else if(type is INamedTypeSymbol namedType && namedType.TypeArguments.Any())
			{
				var args = string.Join(",", namedType.TypeArguments.Select(Relocate));

				string fullName = type.OriginalDefinition.GetFullMetadataName();
				var splitter = fullName.IndexOf('`');
				return $"{Relocate(fullName.Substring(0, splitter))}<{args}>";
			}
			return type.ToDisplayString()
				.Replace("object", "string") // Immutable generator does not allow for object fields, yet.
				;
		}

		private string Relocate(string v)
			=> v
			.Replace("Windows.System", "Immutable.WinSystem")
			.Replace("Windows", "Immutable")
			.Replace("object", "string")  // Immutable generator does not allow for object fields, yet.
			.Replace("System.Collections.Generic.IList", "System.Collections.Immutable.ImmutableList")
			.Replace("System.Collections.Generic.IDictionary", "System.Collections.Immutable.IImmutableDictionary");
	}
}