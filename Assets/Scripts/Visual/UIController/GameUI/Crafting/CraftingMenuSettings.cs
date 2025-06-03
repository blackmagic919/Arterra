using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Item;

namespace WorldConfig.Intrinsic{

/// <summary>
/// Settings controlling the apperance and behavior of the crafting system. The crafting 
/// system allows the player to create items from materials enabling a wider variety of 
/// content and functionality within the game.
/// </summary>
[CreateAssetMenu(menuName = "Settings/Crafting/Options")]
public class Crafting : ScriptableObject{
    /// <summary> The width of the square grid in terms of grid spaces. As recipe entries are stored on 
    /// grid corners, the amount of entries necessary to fill the grid is <c> (GridWidth + 1)^2 </c>. 
    /// If the grid width changes all recipes must be modified to refit the new grid size. </summary>
    public int GridWidth; //3
    /// <summary> How fast the user can place materials in the crafting grid, measured in change in density
    /// per frame. As placement of materials is a continuous process, the speed effects how quickly
    /// the player can craft items. </summary>
    public int CraftSpeed; //200
    /// <summary> When a user's cursor hovers closer to a grid corner, how large the grid corner point grows.
    /// This is used to help the user see where they are placing materials.  </summary>
    public int PointSizeMultiplier; //2
    /// <summary> The IsoValue that is used to create the visual representation of the crafted recipe. Though this is
    /// seperately configurable, it should generally be the same as <see cref="WorldConfig.Quality.Terrain.IsoLevel"/>
    /// to avoid confusion. </summary>
    public uint CraftingIsoValue; //128

    /// <summary> The maximum L1 distance between a recipe entry and the player's crafting grid
    /// for the recipe to be displayed in the crafting menu. The point is to show only the 
    /// closest recipes to the player's input to avoid cluttering the crafting menu. </summary>
    public int MaxRecipeDistance; //64
    /// <summary> The maximum amount of selections that will be displayed under the crafting grid.
    /// This is the maximum amount of recipes that will be shown in case too many recipes are
    /// within <see cref="MaxRecipeDistance"/> of the player's crafting grid. </summary>
    public int NumMaxSelections; //5
    /// <summary> The registry of all recipes that can be crafted within the game. See <see cref="Recipe"/> for more information. </summary>
    public Registry<CraftingRecipe> Recipes; 
}
}
