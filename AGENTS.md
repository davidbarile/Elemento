# Coding guidelines for Elemento (from David's house style)

Match these in all new and changed C# code:

- public class variables: use camelCase with private/protected and PascalCase with public.
- For public, private and protected class variables, add "this." prefix when accessing them inside methods.  Only do this to class variables, not methods.
- When generating new code and prefabs and creating such classes as menu items, inventory items, etc. create classes for these prefabs with public Initialize() and/or Configure() methods to pass them the information they need to populate their fields, images, etc.  When needed, cache these variables in private fields for use in OnPress methods or other event handlers.
- Name scriptable objects for Data containers with "Config" suffix, e.g. ItemConfig, EnemyConfig, etc.
- Serialized data objects (that need to be JSON serialized) should have "Data" suffix, e.g. PlayerData, GameData, etc.
- When creating new List and Dictionary variables, use the shorthand syntax, e.g. "private List<Item> items = new();" instead of "private List<Item> items = new List<Item>();".
- When creating events, use the Action type from System namespace, e.g. "public static Action OnGameOver;".
- When doing [System.Serializable], use [Serializable] instead and import System namespace.
- When referencing an external enum, class, or method, only use the full namespace path if there is a naming conflict.  Otherwise, use the short name and import the static class or namespace at the top of the file.
- When declaring class variables, do not make them equal null, just declare them.  For example, use "private GameManager gameManager;" instead of "private GameManager gameManager = null;".
- Enums should be named with an "E" prefix, e.g. EItemType, EGameState, etc. and their values should be in PascalCase.
- Write false bool checks (!this.isInitialized) instead of  (this.isInitialized== false)
