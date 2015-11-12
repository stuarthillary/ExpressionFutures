# C# Expression API

This project contains C#-specific extensions to the `System.Linq.Expressions` API to support C# language constructs that were added after C# 3.0. It only contains the runtime library; the C# compiler changes required to support assignment of lambda expressions containing those language constructs to an `Expression<TDelegate>` is maintained separately.

## API Basics

The top-level namespace of the API is `Microsoft.CSharp.Expressions`. Unless specified otherwise, all references to types in the description below will assume this namespace.

### CSharpExpression

All C#-specific expression tree node types derive from `CSharpExpression` which in turn derives from `System.Linq.Expressions.Expression`. This type also provides factory methods for the various C#-specific expression tree nodes. Users familiar with the LINQ expression tree API should feel familiar with this design immediately.

In order to distinguish different node kinds, the `CSharpExpression` type exposes an instance property `CSharpNodeType` of enumeration type `CSharpExpressionType`. The `NodeType` property derived from `System.Linq.Expressions.Expression` always returns `System.Linq.Expressions.ExpressionType.Extension`.

### Visitors

Analogous to the LINQ expression tree API, an expression visitor is provided using an abstract base class called `CSharpExpressionVisitor`, which is derived from `System.Linq.Expressions.ExpressionVisitor` and overrides `VisitExtension` to dispatch into protected virtual `Visit` methods for each C# node type. It uses the typical dispatch pattern whereby the `CSharpExpression` types expose an `Accept` method.

### Compilation

Support for runtime compilation through the `Compile` methods on `System.Linq.Expressions.LambdaExpression` and `System.Linq.Expressions.Expression<TDelegate>` is provided by making the C#-specific expression tree nodes reducible. The `Reduce` method returns a more primitive LINQ expression tree which the compiler and interpreter in the LINQ API can deal with. By using this implementation strategy, the LINQ APIs don't have to change.

Note that compilation of asynchronous lambda expressions is supported through `Compile` methods on `AsyncLambdaCSharpExpression` and `AsyncCSharpExpression<TDelegate>`. This works around the limitation of LINQ's lambda expression nodes being sealed types. For more information, read on.

Also note that some language constructs such as indexer initializers introduced in C# 6.0 can't be provided as reducible extensions to the LINQ APIs. For more information, read on.

## Supported Language Features

In the following sections, we'll describe the C# language features that are supported in the `Microsoft.CSharp.Expressions` API, excluding those that are already supported by the LINQ expression APIs.

### C# 3.0

#### Multi-dimensional array initializers

One omission in the LINQ expression API is support for multi-dimensional array initializers. An example of this restriction is shown below:

```csharp
Expression<Func<int[,]>> f = () => new int[2, 2] { { 1, 2 }, { 3, 4 } };
```

This will fail to compile with:

```
error CS0838: An expression tree may not contain a multidimensional array initializer
```

Note that the bounds of the array have to be compile-time constants. Therefore, the number of expected elements is known statically.

In order to lift this restriction, we introduced `NewMultidimensionalArrayInitCSharpExpression` which contains a list of expressions denoting the array elements listed in row-major order. The node is parameterized on an array of `Int32` bounds for the array's dimensions. An example of creating an instance of this node type is shown below:

```csharp
CSharpExpression.NewMultidimensionalArrayInit(
  typeof(int), new[] { 2, 2 },
  Expression.Constant(1), Expression.Constant(2),
  Expression.Constant(3), Expression.Constant(4)
);
```

The `GetExpression(int[])` method can be used to retrieve an expression representing an element in the array. This is useful when analyzing an instance of the node type.

Reduction of the node produces a `Block` expression containing a `NewArrayBounds` expression to instantiate an empty multi-dimensional array, followed by a series of `Assign` binary expressions that assign the elements of the array via `ArrayAccess` index expressions. The expressions representing the elements are evaluated in left-to-right row-major order as the language prescribes.

### C# 4.0

#### Named and optional parameters

An example of this restriction is shown below:

```csharp
Expression<Func<int>> f = () => F(2) + F(y: 3, x: 4);
```

This will fail to compile with:

```
error CS0853: An expression tree may not contain a named argument specification
error CS0854: An expression tree may not contain a call or invocation that uses optional arguments
```

This restriction exists for method calls, delegate invocations, object creation, and indexer lookups.

In order to lift this restriction, we introduce C#-specific variants of `Call`, `Invoke`, `New`, and `Index`. Those APIs are completely analogous to the LINQ equivalents, except for the `Arguments` property's type. Rather than storing a collection of `Expression`s to denote the arguments, we store a collection of `ParameterAssignment` objects.

A `ParameterAssignment` object is created through the `Bind` factory method, analogous to the concept of a `MemberAssignment` created via a `Bind` method in LINQ for use in object initializer expressions. It represents the assignment of an `Expression` to an argument represented by a `ParameterInfo`.

Factory methods for `Call`, `Invoke`, `New`, and `Index` have overloads that accept a `ParameterAssignment[]` or `IEnumerable<ParameterAssignment>` in places where LINQ APIs have `Expression`-based equivalents. In addition, the `Expression`-based overloads exist as well (and hide the LINQ APIs methods) which enables the use of optional parameters without having to create parameter assignments.

All factory methods check whether all required parameters are specified exactly once. Parameter assignments perform assignment compatiblity checks and support implicit quotation of expressions assigned to `Expression<TDelegate>` parameters, analogous to the behavior in the LINQ expression API.

An example of using the API to describe calls with named and optional parameters is shown below:

```csharp
Expression.Add(
  CSharpExpression.Call(
    f,
    Expression.Constant(2)
  ),
  CSharpExpression.Call(
    f,
    CSharpExpression.Bind(y, Expression.Constant(3)),
    CSharpExpression.Bind(x, Expression.Constant(4))
  )
)
```

where `f` is a `MethodInfo` describing `F` and `x` and `y` are `ParameterInfo` objects describing the `x` and `y` parameters of `F`.

An overload of `Bind` taking a `MethodInfo` and `string` is provided for the compiler to emit code against given that it's not directly possible to get a `ParameterInfo` object via `ldtoken` instructions.

Reduction of those nodes first analyzes whether all arguments are supplied in the right order, ignoring those that don't have side-effects upon their evaluation (e.g. `Constant`, `Default`, or `Parameter`). In case the order matches, the corresponding LINQ expression node is returned. If the order doesn't match, a `Block` expression is emitted, containing `Assign` binary expressions that evaluate the argument expressions in the specified order and assign them to `Variable` parameter expressions prior to emitting the underlying LINQ expression node.

Each of those expression types supports by-ref parameters and write-backs to `Parameter`, `Index`, `MemberAccess`, `ArrayIndex`, and `Call` (for `ArrayAccess` via the `Array.Get` method) nodes. Strictly speaking this isn't required for C# but those nodes could be promoted to the LINQ APIs in order to be used by VB as well.

Note that none of those expression types are recognized by the LINQ expression API and compiler as assignable targets. If we want to support assignment expressions, we either have to enlighten the LINQ APIs about the assignability of those new node types (by providing an extensibility story for `set` operations against those) or use a C#-specific version of `Assign`. The use of those nodes in by-ref positions is not supported either, but that's less of an issue for C#-specific expressions given that the C# language does not support passing e.g. indexing expressions by reference.

#### Dynamic

Even though a `Dynamic` node type was added in the .NET 4.0 revision of the LINQ expression APIs, the C# language does not support emitting expression trees containing dynamic operations, as illustrated below:

```csharp
Expression<Action<dynamic>> f = x => Console.WriteLine(x);
```

This will fail to compile with:

```
error CS1963: An expression tree may not contain a dynamic operation
```

Various operations can be performed in a late-bound fashion using `dynamic`, including invocations, invocation of members, lookup of members, indexing, object creation, unary operators, binary operators, and conversions.

In order to support C# `dynamic` in expression trees, we have a few options. We could simply piggyback on the DLR's support for the `Dynamic` expression kind, specifying a `CallSiteBinder` argument using the `Microsoft.CSharp.RuntimeBinder.Binder` factories. While this would work to provide runtime compilation support, it leaves the resulting expression tree in a rather cumbersome shape for expression analysis and translation, e.g. in a LINQ provider. One would have to perform type checks against the `CallSiteBinder` in order to reverse engineer the user intent. This itself is hampered by the fact that the C# runtime binder types are marked as `internal` so C#-specific information is not accessible.

Instead of going down the route of emitting calls to the `Expression.Dynamic` factory, we decided to model the dynamic operations as first-class node types in the C# expression API. This improves the experience for expression analysis and translation by retaining the user intent in the shape of the resulting tree. Reduction of those nodes ultimately falls back to emitting `Dynamic` nodes, thus leveraging the C# runtime binder and the DLR.

All of the `dynamic` support is provided through `DynamicCSharpExpression` which derives from `CSharpExpression` and provides factory methods analogous to the LINQ factories but prefixed with `Dynamic`. For example, `Add` becomes `DynamicAdd`. We could flatten the class hierarchy for the factory methods, though the current design could allow for separating out the `dynamic` support in a separate library. When not referenced, the C# compiler would fail resolving the required factory methods, thus making the `dynamic` feature unavailable in expression trees. This opt-in mechanism could come in handy.

Dynamic node types are mirrored after the `Microsoft.CSharp.RuntimeBinder.Binder` factory methods, so we have dynamically bound nodes for `BinaryOperation`, `Convert`, `GetIndex`, `GetMember`, `Invoke`, `InvokeConstructor`, `InvokeMember`, and `UnaryOperation`. Some of these could be revisited in the C# expression API to get nomenclature that aligns better with the existing LINQ expression API's, e.g. using `Call` in lieu of `InvokeMember`.

Some dynamic operations require `Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo` objects to be passed, describing an argument's name and its behavior such as `ref` or `out` usage. This is modeled via a `DynamicCSharpArgument` that can be created via the `DynamicArgument` factory by specifying a `string`, an `Expression`, and a `CSharpArgumentInfoFlags` value. This node allows for inspection of e.g. the name of the argument during expression analysis but ultimately reduces into a `CSharpArgumentInfo` object. Note that the `CSharpArgumentInfoFlags` are exposed in the C# expression API as-is; this type is publicly accessible anyway.

To illustrate this API, consider the most verbose `DynamicInvokeMember` factory methods, shown below:

```csharp
// Instance call
public static InvokeMemberDynamicCSharpExpression DynamicInvokeMember(Expression @object, string name, IEnumerable<Type> typeArguments, IEnumerable<DynamicCSharpArgument> arguments, CSharpBinderFlags binderFlags, Type context);

// Static call
public static InvokeMemberDynamicCSharpExpression DynamicInvokeMember(Type type, string name, IEnumerable<Type> typeArguments, IEnumerable<DynamicCSharpArgument> arguments, CSharpBinderFlags binderFlags, Type context);
```

Note that a separate method exists for instance and static member invocations. Rather than modeling the receiver type for a static call as a dynamic argument (as done by the C# compiler when emitting a call to for `Binder.InvokeMember`) we decided to hoist this information up to the expression tree node. Effectively, the combination of `type`, `name`, and `typeArguments` is the equivalent of a `MethodInfo` in the statically bound case. This leads to a familiar API for those used to the LINQ expression API for `Call`.

An example of using the API to make a dynamically-bound call is shown below:

```csharp
DynamicCSharpExpression.DynamicInvokeMember(
  typeof(Console),
  "WriteLine",
  x
)
```

Overloads are provided that allow for easy construction of those node types, e.g. as used in the example above. The more verbose equivalent would be:

```csharp
DynamicCSharpExpression.DynamicInvokeMember(
  typeof(Console),
  "WriteLine",
  Array.Empty<Type>(),
  DynamicCSharpExpression.DynamicArgument(
    x,
    null,
    CSharpArgumentInfoFlags.None
  ),
  CSharpBinderFlags.None,
  typeof(Program)
)
```

It goes without saying the the `CSharpExpressionVisitor` allows for visitation of all the nodes types mentioned above.

Reduction of dynamically bound expressions uses a `Dynamic` node underneath. To specialize the emitted expression for each dynamically bound node type, the `DynamicCSharpExpression` base class provides a `ReduceDynamic` method which returns a `CallSiteBinder` and an `IEnumerable<Expression>` with the expressions to pass to the `Dynamic` node. The returned binders are obtained from the `Microsoft.CSharp.RuntimeBinder.Binder` factory methods.

### C# 5.0

#### Async and Await

Lambda expressions with the `async` modifier and `await` expressions are not supported in expressions. For example, consider the following piece of code:

```csharp
Expression<Func<Task<int>>> f = async () => await Task.FromResult(42);
```

This fails to compile with:

```
error CS1989: Async lambda expressions cannot be converted to expression trees
```

##### Async lambdas

Support for asynchronous lambdas is added by the introduction of an `AsyncLambdaCSharpExpression` type and a `AsyncCSharpExpression<TDelegate>` type derived from it. These are analogous to the `LambdaExpression` and `Expression<TDelegate>` types in the LINQ expression API. Their behavior differs both in terms of typing and in terms of compilation.

From a typing point of view, an asynchronous lambda requires its return type to be `void`, `Task`, or `Task<T>` with the `Body` being assignable to `T` in the latter case. In terms of compilation, the `Compile` methods perform a rewrite similar to the one carried out by the C# compiler, as described further on.

Note that the introduction of custom node types for asynchronous lambdas was required in order to provide proper compilation support and to allow for the specialized type checking due to the return type being lifted over `Task<T>`. Alternatively, we could provide for an extensibility store in the LINQ expression API's `Lambda` nodes or push down the asynchronous lambda support the LINQ expression APIs so that VB can also benefit from it.

##### Await expressions

Await expressions are of type `AwaitCSharpExpression` and support the awaiter pattern as described in the C# language specification. As such, a custom `GetAwaiter` method can be specified on the `Await` factory, including support for extension methods. If left unspecified, the factory will try to find a suitable method. The `GetResult` and `IsCompleted` members on the awaiter type are discovered by the factory as well.

The typing of await expressions follows the awaiter pattern and obtains the return type of the `GetResult` method on the awaiter. This allows those nodes to be composed with any existing LINQ expression nodes.

Await expression nodes are not reducible; the reduction of the closest enclosing async lambda is responsible for its reduction into the await pattern within the generated state machine (see further).

##### Example

Prior to discussing the compilation support for asynchronous lambdas with await expressions, let's have a look at an example:

```csharp
CSharpExpression.AsyncLambda<Func<Task<int>>>(
  CSharpExpression.Await(
    Expression.Call(
      fromResult,
      Expression.Constant(42)
    )
  )
)
```

In addition to the various `AsyncLambda` factory overloads, a single `Lambda` overload with an `isAsync` parameter is provided as well:

```csharp
public static Expression<TDelegate> Lambda<TDelegate>(bool isAsync, Expression body, params ParameterExpression[] parameters);
```

This overload is put in place for use by the C# compiler as a stop-gap measure for assignment compatibility of async lambda expressions to `Expression<TDelegate>`. The returned expression is simply an `Expression<TDelegate>` whose body is an `InvocationExpression` wrapping the underlying C#-specific `AsyncCSharpExpression<TDelegate>`. Unless we have an extensibility store for LINQ's `Expression<TDelegate>` we have to use this unnatural pattern (which does not look good at all in the resulting tree of course) to achieve assignment compatilbity. Alternatively, we have to introduce assignment compatibility with `AsyncCSharpExpression<TDelegate>` which would expand the language specification for lambda expressions and still require changes to the LINQ APIs to support implicit quoting of expression arguments assigned to expression-typed parameters.

##### Compilation

Compilation of async lambdas through the `Compile` methods is carried out via the `Reduce` method which reduces the node to a classic LINQ `Expression<TDelegate>`. By having the `Reduce` method perform this reduction, an async lambda can occur in a bigger (synchronous) expression tree. Await expressions in async lambdas get reduced using the awaiter pattern.

Prior to carrying out any reduction, we run the expression through a checker that looks for the use of `Await` expressions in forbidden places, e.g. the `Filter` of a `CatchBlock` node. Like many other steps mentioned below, this step proceeds in a shallow way, i.e. it doesn't recurse into nested lambdas of any kind.

The first step in compiling an async lambda is the creation of the appropriate asynchronous method builder from the `System.Runtime.CompilerServices` namespace. The outermost structure of the generated (synchronous) lambda contains the code to instantiate the builder, invoke the `Start` method, and return its `Task` property (if any).

In order to generate an `IAsyncStateMachine` we use a `RuntimeStateMachine` type added to the `System.Runtime.CompilerServices` namespace in our assembly. This class implements the `IAsyncStateMachine` interface's `MoveNext` method by invoking an `Action` delegate supplied to the constructor. All of the state that has to be kept across await boundaries is made part of the closure of the delegate supplied to the constructor. This way, we avoid having to generate a state machine type on the fly, which would require access to a `TypeBuilder`.

Although generating a custom state machine type would be more efficient (e.g. by using a struct and implementing `SetStateMachine` as well), it would introduce non-trivial complexity, require access to the `TypeBuilder` which is encapsulated in the LINQ lambda compiler, and prevent the same code from working with the expression interpreter as well. Note that the lambda compiler doesn't generate a full-blown display class for closures either (though it could).

The next step in the compilation of an async lambda is the rewrite of the `Body` expression. This proceeds in a few steps. First, we reduce all nodes in the `Body` except for the `Await` nodes. By doing so, we only have to worry about LINQ nodes in the steps that follow. Second, we lower various constructs to a more primitive form in order to support their use in asynchronous lambdas. Those include `Try` expressions with `Await` expressions in any of the `Handlers` or `Finally` or `Fault` expressions. Third, we perform stack spilling in order to be able to await while having intermediate expression evaluation results on the stack. Finally, we transform the resulting lowered `Body` by rewriting all `Await` expressions into the awaiter pattern using a state machine. Some other manipulations of the expression are deemed implementation details and are omitted from this description.

#### C# 6.0

##### Conditional Access Expressions

Conditional access expressions, also known as "null-propagating operators", are not supported in expression trees as shown below:

```csharp
Expression<Func<string, int?>> f = s => s?.Length;
```

This fails to compile with:

```
error CS8072: An expression tree lambda may not contain a null propagating operator.
```

Null-propagation is supported for method invocation, member access, and indexing operations in C#. Delegate invocation using null-propagation is not supported due to syntactic ambiguities by can be achieved by using method invocation syntax to call `Invoke`.

In order to support null-propagating operators, we have a few options. One is to introduce one new node type to perform conditional access to a receiver when performing an operation such as `Call`, `Member`, or `Index`. This would effectively wrap existing nodes in a null-conditional access node. While this approach is tempting, it doesn't compose well with existing node types which expect their input to be non-nullable. One way to make this work would be to refactor the existing node types to separate their receiver (e.g. `MethodCallExpression.Object`) from their target (e.g. `MethodCallExpression.Method`) and arguments (e.g. `MethodCallExpression.Arguments`).

Rather than going down the path of wrapping existing nodes, we chose to model each null-propagating operator as its own new node type. Those node types are `ConditionalMemberAccess`, `ConditionalCall`, `ConditionalIndex`, and `ConditionalInvoke`. Note that the latter node type is strictly speaking not needed due to the syntactic limitation imposed by the C# language; nonetheless, we added a null-propagating invocation which could be emitted for `d?.Invoke()` where `d` is a delegate.

Each null-propagating operator derives from `ConditionalAccessCSharpExpression` which has an `Expression` property representing the conditionally accessed receiver of the operation. The `Type` of the node is the type of the underlying operation but lifted over null. A protected virtual `ReduceAccess` method is used by the `Reduce` method to emit an expression representing the underlying operation after checking the `Expression` for a `null` value.

An example of using those node types is shown below:

```csharp
CSharpExpression.ConditionalProperty(
  s,
  typeof(string).GetProperty("Length")
)
```

The reduction of null-propagating operators emits a `Block` expression with an `Assign` binary expression to assign the result of evaluating the `Expression` to a `Variable` parameter expression. Next, an `IfThen` conditional expression is used to check the variable against a null value. If the variable is not null, the underlying operation is invoked by obtaining the expression from `ReduceAccess`. The result of the underlying operation is lifted to null using a `Convert` unary expression if necessary.

When the receiver of a null-propagating operator is such an operator itself, the emitted code is optimized to nest a series of `IfThen` nodes in order to avoid a bunch of intermediate null checks if any null-propagating operation returned a null value.

##### Indexer Initializers

Indexer (or dictionary) initializers were introduced as an addition to object initializer expressions that were introduced in C# 3.0. They are not supported in expression trees:

```csharp
Expression<Func<Dictionary<string, int>>> f = () => new Dictionary<string, int> { ["Bart"] = 21 };
```

This fails to compile with:

```
error CS8074: An expression tree lambda may not contain a dictionary initializer.
```

Our options to support this are limited given the non-extensible nature of `MemberBinding` nodes in the LINQ expression API. We'd need proper support for reduction of custom binding nodes in order to provide an extension to set of bindings supportyed by the `MemberInitExpression` node.

To work around this limitation as get a glimpse of what it may look like, we've hijacked `ElementInit` and tricked it into calling a specified indexer's `set` method. Unfortunately, `ElementInit` is only supported in `ListInitExpression` nodes so mileage is limited.

An example of this hack is shown below:

```csharp
Expression.ListInit(
  Expression.New(
    typeof(Dictionary<string, int>).GetConstructor(Array.Empty<Type>())
  ),
  CSharpExpression.IndexInit(
    typeof(Dictionary<string, int>).GetProperty("Item"),
    Expression.Constant("Bart"),
    Expression.Constant(21)
  )
)
```

In order to make this work for real, we need extension support for the `MemberBinding` hierarchy of types. This would likely involve a `Reduce` capability that's usable by the expression compiler to emit write operations against the initialized object. This type of reduction is a bit different from the one on `Expression` given that it wouldn't reduce into a node of its own kind, i.e. another `MemberBinding`.

##### Await in Catch and Finally

Our support for `Await` in an `AsyncLambda` (cf. the C# 5.0 section above) includes support for using `Await` in the `Handlers` and/or `Finally` portions of a `Try` expression.

The compilation of such constructs is based on a lowering step where we translate the `Try` expression into a more primitive form with catch and rethrow constructs using `ExceptionDispatchInfo`. We also support pending branches out of the `Body` of a `Try` expression while ensuring the timely execution of the `Finally` handler, if any.

All of this is very similar to the C# compiler approach of supporting `await` in `catch` and `finally` blocks. Two notable differences are our support for `Await` in a `Fault` handler and our support for non-void `Try` expressions which are permitted by the DLR.

#### Statement Trees

Statements have existed in C# since day zero, but have never been supported in expression trees. With the DLR refresh of the LINQ expression API in .NET 4.0, various statement constructs have been modeled, including `Block`, `Try`, `Loop`, etc. The C# compiler has not been updated to support emitting expression trees containing those, as illustrated below:

```csharp
Expression<Action> f = () => {};
```

This fails to compile with:

```
error CS0834: A lambda expression with a statement body cannot be converted to an expression tree
```

Various constructs are properly supported by the DLR nodes already, e.g. `Block`, `Try`, `IfThenElse`, and `Switch`. Note that the example shown above may require some tweaks to those nodes, e.g. supporting a `Block` with no statements inside of it.

However, some node types are not supported directly or too primitive to compile to. In particular, `Loop` is not a great compilation target for `while`, `do`, `for`, and `foreach`. Similarly, `Try` is not a great compilation target for `using`. Instead, we like to model those C# statements as their own nodes.

Each statement node derives from `CSharpStatement` which ensures that the `Type` of the expression is reported as `void`. It does so by overriding the `Reduce` method and delegating the reduction step to a `ReduceCore` method, wrapping its result in a `void`-typed `Block` expression if needed.

Note that factories for statements are exposed on the `CSharpExpression` class. Alternatively, we could move those to the `CSharpStatement` class in order to provide for an opt-in mechanism by moving the statement node implementations to a separate assembly (similar to the approach taken for `dynamic`).

##### Loops

We provide support for various loop constructs, including `while`, `do`, `for`, and `foreach`. Each of these nodes derives from `LoopCSharpStatement` which exposes `Body`, `BreakLabel`, and `ContinueLabel` properties. The loops that rely on a condition are derived from a `ConditionalLoopCSharpStatement` base class which exposes a `Test` property holding the expression representing the loop condition.

###### While

The `WhileCSharpStatement` node represents a `while` conditional loop deriving from `ConditionalLoopCSharpStatement`. It reduces into a `Loop` expression with an `IfThen` expression inside to check for loop termination conditions.

An example of creating a `While` expression is shown below:

```csharp
CSharpExpression.While(
  Expression.LessThan(i, Expression.Constant(10)),
  Expression.Block(
    Expression.Call(writeLine, i),
    Expression.PostIncrementAssign(i)
  )
)
```

###### Do

The `DoCSharpStatement` node represents a `do` conditional loop deriving from `ConditionalLoopCSharpStatement`. It reduces into a `Block` expression holding the `Body` of the loop, an `IfThen` check for the loop termination condition, and various `Label` expressions denoting the `break` and `continue` labels.

An example of creating a `Do` expression is shown below:

```csharp
CSharpExpression.Do(
  Expression.Block(
    Expression.Call(writeLine, i),
    Expression.PostIncrementAssign(i)
  ),
  Expression.LessThan(i, Expression.Constant(10))
)
```

###### For

The `ForCSharpStatement` node represents a `for` loop deriving from `ConditionalLoopCSharpStatement`. It reduces into a `Block` expression performing the initialization steps, executing the `Body` of the loop, checking for the loop termination condition, executing the iterators, and various `Label` expressions denoting the `break` and `continue` labels.

To create a `For` loop node, one specifies zero or more initializers, an optional `Boolean` loop termination condition, and zero or more iterator expressions. The initializers are modeled as `Binary` expression nodes whose kind should be `Assign` and whose `Left` property should be of the `Parameter` kind.

An example of creating a `For` expression is shown below:

```csharp
CSharpExpression.For(
  new[] { Expression.Assign(x, Expression.Constant(0)) },
  Expression.LessThan(i, Expression.Constant(10)),
  new[] { Expression.PostIncrementAssign(i) },
  Expression.Call(writeLine, i)
)
```

###### ForEach

The `ForEachLoopCSharpStatement` node represents a `foreach` loop deriving from `LoopCSharpStatement`. It supports the enumerator pattern as specified in the C# language specification. Currently, the factory methods perform lookups for the `GetEnumerator`, `MoveNext`, `Current`, and `Dispose` members conform the iterator pattern. Overloads could be specified that specify those members using reflection objects.

Reduction of a `ForEach` node uses a strategy similar to the C# compiler's. Special cases are provided for enumeration over arrays, strings, or objects implementing `IEnumerable` or `IEnumerable<T>`. The general case supports any enumerator pattern.

Conform the C# specification, the reduced expression emits a call to `IDisposable.Dispose` if the enumerator implements `IDisposable`. It avoids boxing by calling the method implementing `IDisposable.Dispose` in case the enumerator is a value type. It also supports a conversion of the `Current` property of the enumerator to the type of the specified loop variable.

An example of creating a `ForEach` expression is shown below:

```csharp
CSharpExpression.ForEach(
  x,
  Expression.Constant(new[] { 1, 2, 3 }),
  Expression.Call(writeLine, x)
)
```

where `x` is a `ParameterExpression` of type `int`. Overloads are provided which allow the specification of `break` and `continue` labels.

Note that the factory methods don't check for assignment to the iteration variable or passing the iteration variable in a `ref` or `out` parameter. Doing so would require a visit of the specified `Body`, which could lead to the early reduction of `Extension` nodes (too eager) or skip those nodes to avoid the reduction (too shallow). We could still add such a check to the `Reduce` code path where reduction of `Extension` nodes is carried out anyway.

##### Using

The `UsingCSharpStatement` node represents a `using` statement. It contains a `Variable` which is a `Parameter` node whose type should be assignment compatible with `IDisposable`. The `Resource` property holds the expression representing the resource to dispose after the execution of the `Body`.

Reduction of a `Using` node consists of a `Block` evaluating the `Resource` and assigning it to the `Variable`, followed by a `TryFinally` expression which executes the `Body` in the protected region and disposes the resource held in the `Variable` (after an `IfThen` null-check for non-value types) in the finally handler. Boxing of the resource held in the `Variable` is avoided in case its type is a value type.

An example of creating a `Using` expression is shown below:

```csharp
CSharpExpression.Using(
  fs,
  Expression.Call(fileOpen, "foo.txt"),
  Expression.Call(printAllLines, fs)
)
```

where `fs` is a `ParameterExpression` of type `FileStream`.

Note that the factory methods don't check for assignment to the resource variable or passing the resource variable in a `ref` or `out` parameter. Doing so would require a visit of the specified `Body`, which could lead to the early reduction of `Extension` nodes (too eager) or skip those nodes to avoid the reduction (too shallow). We could still add such a check to the `Reduce` code path where reduction of `Extension` nodes is carried out anyway.

##### Lock

The `LockCSharpStatement` node represents a `lock` statement. It contains an `Expression` to synchronize on prior to executing a `Body` expression. Factory methods ensure that the specified `Expression` is not a value type which would incur boxing.

Reduction of a `Lock` node consists of a `Block` containing a call to the `Monitor.Enter(object, ref bool)` method, followed by a `TryFinally` expression containing the execution of the `Body` in the protected region and a call to `Monitor.Exit(object)` in the finally handler. A `lockTaken` variable is introduced to perform an `IfThen` check prior to calling `Monitor.Exit(object)`. This is completely analogous to the code emitted by the C# compiler.

An example of creating a `Lock` expression is shown below:

```csharp
CSharpExpression.Lock(
  Expression.Constant(gate),
  Expression.Call(foo)
)
```