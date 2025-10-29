# Replace FluentAssertions with xUnit Assertions

## Overview

Replace the FluentAssertions library with standard xUnit assertions across all test files. Where direct replacement is not feasible, use NFluent library as an alternative.

## Objectives

1. Remove dependency on FluentAssertions.Json package
2. Replace all FluentAssertions assertions with xUnit equivalents
3. Introduce NFluent package only for scenarios where xUnit cannot provide equivalent functionality
4. Maintain identical test behavior and coverage
5. Preserve test readability and maintainability

## Scope

### Test Files to Modify

| File | FluentAssertions Usage | Complexity |
|------|----------------------|------------|
| MainWindowViewModelTests.cs | High (50+ usages) | Medium |
| TestHelpers.cs | Low (7 usages) | Low |
| JsonRepairingReaderTests.cs | Low (5 usages) | Low |
| TaskMigratorTests.cs | None detected | None |
| UnitTest1.cs | None detected | None |

### Package References to Update

The project file `Unlimotion.Test.csproj` currently references:
- FluentAssertions.Json - to be removed
- NFluent - to be added (conditionally, only if needed)

## Assertion Mapping Strategy

### Direct xUnit Replacements

The following FluentAssertions patterns have direct xUnit equivalents:

| FluentAssertions | xUnit Equivalent | Notes |
|-----------------|------------------|-------|
| `value.Should().Be(expected)` | `Assert.Equal(expected, value)` | Simple equality |
| `value.Should().BeTrue()` | `Assert.True(value)` | Boolean true |
| `value.Should().BeFalse()` | `Assert.False(value)` | Boolean false |
| `value.Should().BeNull()` | `Assert.Null(value)` | Null check |
| `value.Should().NotBeNull()` | `Assert.NotNull(value)` | Not null check |
| `collection.Should().BeEmpty()` | `Assert.Empty(collection)` | Empty collection |
| `collection.Should().NotBeEmpty()` | `Assert.NotEmpty(collection)` | Non-empty collection |
| `collection.Should().Contain(item)` | `Assert.Contains(item, collection)` | Collection membership |
| `collection.Should().NotContain(item)` | `Assert.DoesNotContain(item, collection)` | Collection non-membership |
| `collection.Should().HaveCount(n)` | `Assert.Equal(n, collection.Count)` | Collection count |
| `collection.Should().ContainSingle(predicate)` | `Assert.Single(collection, predicate)` | Single matching item |
| `string.Should().StartWith(prefix)` | `Assert.StartsWith(prefix, string)` | String prefix |

### Complex Scenarios Requiring NFluent

Certain assertion patterns may require NFluent if they cannot be elegantly expressed with xUnit:

| Scenario | Consideration | Decision Criteria |
|----------|--------------|------------------|
| Fluent chaining with multiple conditions | xUnit requires multiple separate assertions | Use NFluent if readability significantly degrades |
| Complex JSON comparison | Currently uses FluentAssertions.Json | Evaluate if standard equality suffices or NFluent needed |
| Custom error messages with fluent syntax | xUnit supports custom messages differently | Prefer xUnit unless message formatting is critical |

## Implementation Approach

### Phase 1: Simple Replacements

Transform all straightforward FluentAssertions calls to xUnit equivalents in each test file.

#### TestHelpers.cs

Replace assertion helper methods:
- `ActionNotCreateItems`: Replace `Should().Be()` with `Assert.Equal()`
- `CreateAndReturnNewTaskItem`: Replace `Should().Be()` with `Assert.Equal()`
- `ShouldHaveOnlyTitleChanged`: Replace fluent chain with xUnit assertions
- `ShouldContainOnlyDifference`: Replace `Should().ContainSingle()` with `Assert.Single()`
- `AssertTaskLink`: Replace `Should().Contain()` with `Assert.Contains()`
- `AssertTaskExistsOnDisk`: Replace `Should().BeTrue()` with `Assert.True()`
- `AssertTaskNotExistsOnDisk`: Replace `Should().BeFalse()` with `Assert.False()`

#### JsonRepairingReaderTests.cs

Replace all string equality assertions:
- Transform `repair.Should().Be(expected)` to `Assert.Equal(expected, repair)`

#### MainWindowViewModelTests.cs

Replace all FluentAssertions calls following the mapping table above. Key patterns:
- Collection membership checks
- Null/NotNull checks
- Boolean assertions
- Equality checks
- Count assertions

### Phase 2: Complex Scenario Evaluation

After Phase 1, evaluate any remaining assertions that prove difficult to replace:

1. **Analyze readability impact**: Compare xUnit version with original FluentAssertions
2. **Assess NFluent necessity**: Determine if NFluent provides clear advantage
3. **Document decision**: Record rationale for using NFluent vs xUnit

### Phase 3: Package Management

Update project dependencies:

1. Remove FluentAssertions.Json package reference from `Unlimotion.Test.csproj`
2. Add NFluent package reference only if Phase 2 identifies concrete need
3. Remove FluentAssertions using directives from all test files
4. Add NFluent using directive only where needed

## Migration Principles

### Maintain Test Intent

Each assertion replacement must preserve the exact semantic meaning:
- Same failure conditions
- Equivalent error messages (where practical)
- Identical test behavior

### Parameter Order Convention

xUnit follows the pattern: `Assert.Method(expected, actual)`

FluentAssertions follows: `actual.Should().Be(expected)`

Ensure correct parameter ordering during transformation.

### Custom Message Handling

FluentAssertions format: `value.Should().Be(expected, "because {0}", reason)`

xUnit equivalent: Custom messages are less common but can be achieved through assertion wrapping or inline string interpolation in assertion statements.

Where custom messages are critical for test diagnostics, preserve them using appropriate xUnit patterns.

## Validation Criteria

### Functional Validation

All tests must:
- Compile without errors
- Pass with identical behavior to original FluentAssertions version
- Maintain same failure detection capability

### Code Quality

Resulting test code must:
- Remain readable and maintainable
- Follow xUnit assertion conventions
- Not introduce unnecessary complexity
- Preserve or improve assertion clarity

### Dependency Cleanliness

Final state must:
- Have no FluentAssertions.Json dependency
- Include NFluent only if demonstrable benefit exists
- Minimize test framework dependencies

## Risk Assessment

### Low Risk

- Simple equality and boolean assertions
- Collection membership checks
- Null checks

### Medium Risk

- Multi-condition assertion chains
- Complex collection validation
- Custom assertion messages

### Mitigation

- Perform incremental replacement with test execution between changes
- Maintain test coverage metrics throughout migration
- Code review each file's changes independently

## Expected Outcomes

### Primary Benefits

1. **Reduced dependencies**: Fewer external packages to maintain
2. **Framework consistency**: Align with xUnit-native assertion patterns
3. **Simplicity**: Standard xUnit patterns are widely understood

### Acceptance Criteria

- All tests compile and pass
- No FluentAssertions references remain
- Test readability is maintained or improved
- NFluent is added only if strictly necessary
- Code review approval confirms quality standards- NFluent is added only if strictly necessary
