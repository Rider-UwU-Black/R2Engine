using System.Globalization;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace R2Engine.Editor.Scene;

// First lowering target: constant local motion on standalone decorative meshes.
// No user assembly is loaded or executed while cooking.
public sealed record Ps2ScriptTimer(float Initial, float Threshold, uint Comparison,
    Vector3 ElsePosition, Vector3 ElseRotation);
public sealed record Ps2ScriptStartup(Vector3 Position, Vector3 Rotation);
public readonly record struct Ps2ScriptMotion(Vector3 Position, Vector3 Rotation, Ps2ScriptTimer? Timer = null,
    Ps2ScriptStartup? Startup = null);

public static class Ps2ScriptCompiler
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(ScriptBehaviour).Assembly.Location).Distinct()
            .Select(p => MetadataReference.CreateFromFile(p)).ToArray());

    public static Ps2ScriptMotion Compile(string source, IReadOnlyDictionary<string, string> overrides,
        IEnumerable<InputActionBinding>? actions = null)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Ps2ScriptCheck", new[] { tree }, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var error = compilation.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error != null) throw new InvalidOperationException(error.ToString());
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        Require(!root.ContainsDirectives && root.AttributeLists.Count == 0 &&
            root.Usings.All(u => u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) &&
                u.Name?.ToString() is "System.Numerics" or "R2Engine.Editor.Scene"),
            "Only ordinary System.Numerics and R2Engine.Editor.Scene imports are supported.");
        Require(root.Members.Count == 1 && root.Members[0] is ClassDeclarationSyntax,
            "Use one top-level ScriptBehaviour class (no namespace or helper types yet).");
        var type = (ClassDeclarationSyntax)root.Members[0];
        Require(type.AttributeLists.Count == 0 && type.TypeParameterList == null && type.ParameterList == null &&
            type.BaseList?.Types.Count == 1 && type.BaseList.Types[0].Type.ToString() == "ScriptBehaviour" &&
            type.Modifiers.All(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.SealedKeyword)),
            "Use a plain public ScriptBehaviour class.");
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        string? timerName = null;
        float timerInitial = 0;
        string? toggleName = null;
        bool toggleInitial = false;
        float Finite(float value)
        { Require(float.IsFinite(value), "Script values must be finite."); return value; }
        float Constant(ExpressionSyntax expression)
        {
            var value = model.GetConstantValue(expression);
            Require(value.HasValue && value.Value is float, "Use float constants (for example 30f).");
            return Finite((float)value.Value!);
        }
        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
        {
            Require(field.AttributeLists.Count == 0 && field.Declaration.Type.ToString() is "float" or "bool" &&
                field.Modifiers.Count == 1 && (field.Modifiers[0].IsKind(SyntaxKind.PublicKeyword) ||
                    field.Modifiers[0].IsKind(SyntaxKind.PrivateKeyword)),
                "Use public float settings and optionally one private float timer or private bool toggle.");
            foreach (var variable in field.Declaration.Variables)
            {
                if (field.Declaration.Type.ToString() == "bool")
                {
                    Require(field.Modifiers[0].IsKind(SyntaxKind.PrivateKeyword) && toggleName == null && timerName == null,
                        "Only one private bool toggle is supported; it cannot be combined with a timer.");
                    toggleName = variable.Identifier.ValueText;
                    if (variable.Initializer != null)
                    {
                        var initial = model.GetConstantValue(variable.Initializer.Value);
                        Require(initial.HasValue && initial.Value is bool, "Toggle initializer must be a constant bool.");
                        toggleInitial = (bool)initial.Value!;
                    }
                    continue;
                }
                float value = variable.Initializer == null ? 0 : Constant(variable.Initializer.Value);
                if (field.Modifiers[0].IsKind(SyntaxKind.PrivateKeyword))
                {
                    Require(timerName == null && toggleName == null, "Use one private timer OR one private bool toggle.");
                    timerName = variable.Identifier.ValueText;
                    timerInitial = value;
                    continue;
                }
                if (overrides.TryGetValue(variable.Identifier.ValueText, out var configured))
                {
                    Require(float.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out value),
                        $"Invalid float override for {variable.Identifier.ValueText}.");
                }
                values.Add(variable.Identifier.ValueText, Finite(value));
            }
        }
        Require(overrides.Keys.All(values.ContainsKey), "Remove stale or unsupported Inspector field overrides.");
        var methods = type.Members.OfType<MethodDeclarationSyntax>().ToArray();
        if (methods.Length == 2 && methods.Count(m => m.Identifier.ValueText == "Start") == 1 &&
            methods.Count(m => m.Identifier.ValueText == "Update") == 1 &&
            type.Members.Count == type.Members.OfType<FieldDeclarationSyntax>().Count() + 2)
        {
            string OnlyLifecycle(string name) => root.ReplaceNode(type,
                type.WithMembers(SyntaxFactory.List(type.Members.Where(m =>
                    (m is not MethodDeclarationSyntax methodNode || methodNode.Identifier.ValueText == name) &&
                    !(name == "Start" && m is FieldDeclarationSyntax fieldNode && fieldNode.Modifiers.Any(t => t.IsKind(SyntaxKind.PrivateKeyword))))))).ToFullString();
            var startup = Compile(OnlyLifecycle("Start"), overrides, actions);
            var update = Compile(OnlyLifecycle("Update"), overrides, actions);
            if (update.Timer?.Comparison is >= 1 and <= 6 or 12 or 13)
                return update with { Startup = new(startup.Timer!.ElsePosition, startup.Timer.ElseRotation) };
            Require(update.Timer == null || (update.Timer.Comparison == 7 &&
                update.Timer.ElsePosition == Vector3.Zero && update.Timer.ElseRotation == Vector3.Zero),
                "Start + Update supports unconditional motion, a timer, a toggle, or one held action with no else motion.");
            return update with { Timer = update.Timer == null ? startup.Timer :
                startup.Timer! with { Comparison = 19, Threshold = update.Timer.Threshold } };
        }
        if (methods.Length == 2 && methods.Count(m => m.Identifier.ValueText == "OnTriggerEnter") == 1 &&
            methods.Count(m => m.Identifier.ValueText == "OnTriggerExit") == 1 &&
            type.Members.Count == type.Members.OfType<FieldDeclarationSyntax>().Count() + 2)
        {
            // Validate/lower each callback through the same strict single-method
            // path, retaining class/field checks and never executing user code.
            string Only(string name) => root.ReplaceNode(type,
                type.WithMembers(SyntaxFactory.List(type.Members.Where(m =>
                    m is not MethodDeclarationSyntax methodNode || methodNode.Identifier.ValueText == name)))).ToFullString();
            var enter = Compile(Only("OnTriggerEnter"), overrides, actions);
            var exit = Compile(Only("OnTriggerExit"), overrides, actions);
            return enter with { Timer = new(0, 1, 11, exit.Position, exit.Rotation) };
        }
        Require(methods.Length == 1 && type.Members.Count == type.Members.OfType<FieldDeclarationSyntax>().Count() + 1,
            "Use one supported Update or trigger callback, or a paired OnTriggerEnter/OnTriggerExit. Other method combinations are unsupported.");
        var method = methods[0];
        bool triggerEntry = method.Identifier.ValueText == "OnTriggerEnter";
        bool triggerExit = method.Identifier.ValueText == "OnTriggerExit";
        bool startupMethod = method.Identifier.ValueText == "Start";
        Require((method.Identifier.ValueText == "Update" || triggerEntry || triggerExit || startupMethod) && method.AttributeLists.Count == 0 &&
            method.Modifiers.Count == 2 && method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)) &&
            method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) && method.Body != null &&
            method.ParameterList.Parameters.Count == (startupMethod ? 0 : 1),
            "Use a public override Update(float deltaTime), OnTriggerEnter or OnTriggerExit(GameObject other), with a block body.");
        string dt = startupMethod ? "" : method.ParameterList.Parameters[0].Identifier.ValueText;
        Require(!values.ContainsKey(dt) && timerName != dt && toggleName != dt, "The deltaTime parameter must not shadow a field.");
        float Scalar(ExpressionSyntax e)
        {
            if (e is ParenthesizedExpressionSyntax p) return Scalar(p.Expression);
            if (e is IdentifierNameSyntax id && values.TryGetValue(id.Identifier.ValueText, out float v)) return v;
            return Constant(e);
        }
        Vector3 Vector(ExpressionSyntax e)
        {
            if (e is ParenthesizedExpressionSyntax p) return Vector(p.Expression);
            if (e is MemberAccessExpressionSyntax member && member.Expression.ToString() == "Vector3")
                return member.Name.Identifier.ValueText switch
                {
                    "UnitX" => Vector3.UnitX, "UnitY" => Vector3.UnitY, "UnitZ" => Vector3.UnitZ,
                    "Zero" => Vector3.Zero, "One" => Vector3.One,
                    _ => throw new InvalidOperationException("Unsupported Vector3 constant.")
                };
            if (e is ObjectCreationExpressionSyntax create && create.Type.ToString() == "Vector3" &&
                create.Initializer == null && create.ArgumentList?.Arguments.Count == 3 &&
                create.ArgumentList.Arguments.All(a => a.NameColon == null && a.RefKindKeyword.IsKind(SyntaxKind.None)))
                return new(Scalar(create.ArgumentList.Arguments[0].Expression),
                    Scalar(create.ArgumentList.Arguments[1].Expression), Scalar(create.ArgumentList.Arguments[2].Expression));
            if (e is BinaryExpressionSyntax multiply && multiply.IsKind(SyntaxKind.MultiplyExpression))
                return Vector(multiply.Left) * Scalar(multiply.Right);
            throw new InvalidOperationException("Use Vector3.UnitX/Y/Z or new Vector3(x, y, z), optionally multiplied by a float field.");
        }
        Ps2ScriptMotion Motion(IEnumerable<StatementSyntax> statements, bool allowEmpty = false, bool discrete = false)
        {
          Vector3 position = default, rotation = default;
          var targets = new HashSet<string>();
          foreach (var statement in statements)
          {
            Require(statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax },
                "Only Transform.Position/Rotation += velocity * deltaTime statements are supported.");
            var assignment = (AssignmentExpressionSyntax)((ExpressionStatementSyntax)statement).Expression;
            string target = assignment.Left.ToString();
            Require(assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                target is "Transform.Position" or "Transform.Rotation" && targets.Add(target),
                "Use at most one += statement per Transform.Position/Rotation.");
            Require(discrete || (assignment.Right is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.MultiplyExpression) &&
                b.Right is IdentifierNameSyntax name && name.Identifier.ValueText == dt),
                "The last factor must be deltaTime, e.g. Vector3.UnitY * Speed * deltaTime.");
            var velocity = Vector(discrete ? assignment.Right : ((BinaryExpressionSyntax)assignment.Right).Left);
            Finite(velocity.X); Finite(velocity.Y); Finite(velocity.Z);
            if (target == "Transform.Position") position = velocity; else rotation = velocity;
          }
          Require(allowEmpty || targets.Count > 0, "Update must contain a supported motion statement.");
          return new(position, rotation);
        }
        if (startupMethod)
        {
            Require(timerName == null && toggleName == null, "Start scripts cannot have private state yet.");
            var initial = Motion(method.Body!.Statements, discrete: true);
            return new(Vector3.Zero, Vector3.Zero, new(0, 0, 18, initial.Position, initial.Rotation));
        }
        if (triggerEntry || triggerExit)
        {
            Require(timerName == null && toggleName == null, "Trigger callbacks cannot have private state yet.");
            var statements = method.Body!.Statements;
            Require(statements.Count >= 2 && statements[0] is IfStatementSyntax guard && guard.Else == null &&
                guard.Condition is BinaryExpressionSyntax triggerComparison && triggerComparison.IsKind(SyntaxKind.EqualsExpression) &&
                triggerComparison.Right.IsKind(SyntaxKind.NullLiteralExpression) &&
                triggerComparison.Left is InvocationExpressionSyntax call && call.ArgumentList.Arguments.Count == 0 &&
                call.Expression is MemberAccessExpressionSyntax access && access.Expression is IdentifierNameSyntax other &&
                other.Identifier.ValueText == dt && access.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "GetComponent" && generic.TypeArgumentList.Arguments.Count == 1 &&
                generic.TypeArgumentList.Arguments[0].ToString() == "PlayerController" &&
                (guard.Statement is ReturnStatementSyntax { Expression: null } ||
                    guard.Statement is BlockSyntax block && block.Statements.Count == 1 && block.Statements[0] is ReturnStatementSyntax { Expression: null }),
                "Start each trigger callback with: if (other.GetComponent<PlayerController>() == null) return; Only player callbacks are supported.");
            var motion = Motion(statements.Skip(1), discrete: true);
            Require(motion.Position == Vector3.Zero, "This trigger slice supports fixed rotation only, not moving the trigger.");
            return motion with { Timer = new(0, 1, triggerExit ? 10u : 9u, Vector3.Zero, Vector3.Zero) };
        }
        IEnumerable<StatementSyntax> Statements(StatementSyntax s) =>
            s is BlockSyntax block ? block.Statements : new[] { s };
        (int Button, bool Pressed, bool Released) ResolveButton(ExpressionSyntax expression)
        {
            Require(expression is InvocationExpressionSyntax call &&
                call.Expression.ToString() is "RuntimeInput.IsActionDown" or "RuntimeInput.WasActionPressed" or "RuntimeInput.WasActionReleased" && call.ArgumentList.Arguments.Count == 1 &&
                call.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression),
                "Use RuntimeInput.IsActionDown, WasActionPressed, or WasActionReleased with one literal action name. Combined conditions are not supported.");
            var inputCall = (InvocationExpressionSyntax)expression;
            bool pressed = inputCall.Expression.ToString() == "RuntimeInput.WasActionPressed";
            string actionName = ((LiteralExpressionSyntax)inputCall.ArgumentList.Arguments[0].Expression).Token.ValueText;
            var bindings = new[] { new InputActionBinding { Name = "Interact", Type = InputActionType.Button, ControllerButton = 0 } }
                .Concat(actions ?? InputActionBinding.CreateDefaults());
            var binding = bindings.LastOrDefault(a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
            Require(binding != null && binding.Type == InputActionType.Button && binding.ControllerButton >= 0 && binding.ControllerButton <= 15,
                $"Input action '{actionName}' needs a Button action with a PS2 controller button index 0-15 in Project Settings.");
            return (binding!.ControllerButton, pressed, inputCall.Expression.ToString() == "RuntimeInput.WasActionReleased");
        }
        if (toggleName != null)
        {
            var bodyStatements = method.Body!.Statements;
            Require(bodyStatements.Count == 2 && bodyStatements[0] is IfStatementSyntax && bodyStatements[1] is IfStatementSyntax,
                "Toggle Update must contain a press-controlled bool flip followed by if (toggle) motion.");
            var pressIf = (IfStatementSyntax)bodyStatements[0];
            var action = ResolveButton(pressIf.Condition);
            var flipStatements = Statements(pressIf.Statement).ToArray();
            Require(action.Pressed && pressIf.Else == null && flipStatements.Length == 1 &&
                flipStatements[0] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax flip } &&
                flip.IsKind(SyntaxKind.SimpleAssignmentExpression) && flip.Left is IdentifierNameSyntax lhs && lhs.Identifier.ValueText == toggleName &&
                flip.Right is PrefixUnaryExpressionSyntax not && not.IsKind(SyntaxKind.LogicalNotExpression) &&
                not.Operand is IdentifierNameSyntax rhs && rhs.Identifier.ValueText == toggleName,
                "Use if (RuntimeInput.WasActionPressed(\"Name\")) toggle = !toggle;");
            var stateIf = (IfStatementSyntax)bodyStatements[1];
            Require(stateIf.Condition is IdentifierNameSyntax state && state.Identifier.ValueText == toggleName,
                "The second condition must be the private toggle field.");
            var on = Motion(Statements(stateIf.Statement), allowEmpty: true);
            var off = stateIf.Else == null ? default : Motion(Statements(stateIf.Else.Statement), allowEmpty: true);
            return on with { Timer = new(toggleInitial ? 1 : 0, action.Button, 12, off.Position, off.Rotation) };
        }
        if (timerName == null && method.Body!.Statements is { Count: 2 } dualBody && dualBody.All(s => s is IfStatementSyntax))
        {
            Ps2ScriptMotion[] steps = new Ps2ScriptMotion[2];
            int[] buttons = new int[2];
            int releaseFlags = 0;
            int heldCount = 0;
            for (int i = 0; i < 2; i++)
            {
                Require(dualBody[i] is IfStatementSyntax { Else: null },
                    "Two-action scripts require two independent input if statements without else.");
                var branch = (IfStatementSyntax)dualBody[i];
                var action = ResolveButton(branch.Condition);
                bool held = !action.Pressed && !action.Released;
                if (held) heldCount++;
                if (action.Released) releaseFlags |= 1 << (8 + i);
                buttons[i] = action.Button;
                steps[i] = Motion(Statements(branch.Statement), allowEmpty: true, discrete: !held);
            }
            Require(heldCount != 1, "Use two held actions or two press/release actions; mixing held and edge actions is not supported yet.");
            return steps[0] with { Timer = new(0, buttons[0] | (buttons[1] << 4) | releaseFlags, heldCount == 2 ? 17u : releaseFlags == 0 ? 14u : 16u, steps[1].Position, steps[1].Rotation) };
        }
        if (timerName == null && method.Body!.Statements is { Count: 1 } inputBody && inputBody[0] is IfStatementSyntax inputIf)
        {
            var action = ResolveButton(inputIf.Condition);
            bool pressed = action.Pressed || action.Released;
            Require(!pressed || inputIf.Else == null, "One-shot input scripts do not support else yet.");
            var held = Motion(Statements(inputIf.Statement), allowEmpty: true, discrete: pressed);
            var released = inputIf.Else == null ? default : Motion(Statements(inputIf.Else.Statement), allowEmpty: true);
            // v31 extends the v30 condition record: opcode 7 = held button;
            // threshold stores an exact integer controller index, elapsed is unused.
            return held with { Timer = new(0, action.Button, action.Released ? 15u : pressed ? 8u : 7u, released.Position, released.Rotation) };
        }
        if (timerName == null) return Motion(method.Body!.Statements);
        var body = method.Body!.Statements;
        bool repeating = body.Count == 3;
        Require((body.Count == 2 || repeating) && body[0] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax tick } &&
            tick.IsKind(SyntaxKind.AddAssignmentExpression) && tick.Left is IdentifierNameSyntax left &&
            left.Identifier.ValueText == timerName && tick.Right is IdentifierNameSyntax right && right.Identifier.ValueText == dt &&
            body[repeating ? 2 : 1] is IfStatementSyntax,
            "Timer Update must contain timer += deltaTime; followed by one if/optional else motion branch.");
        var condition = (IfStatementSyntax)body[repeating ? 2 : 1];
        Require(condition.Condition is BinaryExpressionSyntax compare && compare.Left is IdentifierNameSyntax timer &&
            timer.Identifier.ValueText == timerName,
            "Compare the timer on the left against a float constant or public setting on the right.");
        var comparison = (BinaryExpressionSyntax)condition.Condition;
        uint operation = comparison.Kind() switch
        {
            SyntaxKind.LessThanExpression => 1u, SyntaxKind.LessThanOrEqualExpression => 2u,
            SyntaxKind.GreaterThanExpression => 3u, SyntaxKind.GreaterThanOrEqualExpression => 4u,
            SyntaxKind.EqualsExpression => 5u, SyntaxKind.NotEqualsExpression => 6u,
            _ => throw new InvalidOperationException("Supported timer comparisons: <, <=, >, >=, ==, !=.")
        };
        float threshold = Scalar(comparison.Right);
        if (repeating)
        {
            Require(operation == 1 && threshold > 0 && float.IsFinite(threshold * 2f) && timerInitial >= 0,
                "Repeating timers need a positive finite half-period and if (timer < HalfSeconds).");
            Require(body[1] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax wrap } &&
                wrap.IsKind(SyntaxKind.ModuloAssignmentExpression) && wrap.Left is IdentifierNameSyntax wrapName &&
                wrapName.Identifier.ValueText == timerName && wrap.Right is BinaryExpressionSyntax period &&
                period.IsKind(SyntaxKind.MultiplyExpression) && Scalar(period.Left) == threshold && Scalar(period.Right) == 2f,
                "Use timer %= HalfSeconds * 2f; between timer advancement and the motion branch.");
            operation = 13;
        }
        var whenTrue = Motion(Statements(condition.Statement), allowEmpty: true);
        var whenFalse = condition.Else == null ? default : Motion(Statements(condition.Else.Statement), allowEmpty: true);
        return whenTrue with { Timer = new(timerInitial, threshold, operation, whenFalse.Position, whenFalse.Rotation) };
    }
}
