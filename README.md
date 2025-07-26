# Arterra
Arterra is a procedurally generated 3D sandbox-style game built with Unity. Users are permitted to explore vast landscapes populated by unique generation and perhaps find secret discoveries and create their own story. It triumphs an avant-garde world representation that enables users to interact with far more components(clouds, water, air) than its competitors for a fun and refreshing experience. Arterra's built-in generation is set to resemble the natural world, but Users are free to **define their own custom generation**, shaping the world they will explore. With stunning graphics and limitless opportunity, what will your adventure be?

### Trailer
[![Arterra Trailer](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/ArterraTrailer.png)](https://www.youtube.com/watch?v=VQky5HlDAYM)

*Requirements*:
- 4 gB VRAM(Video-RAM)
- 3 gB RAM

*Developed By*: 
- Jonathan Liu

## Generation
  Arterra employs the *Marching Cubes* algorithm developed by Lorensen and Cline in 1987 to transform its world representation(a 3D map of scalar information) into a visual mesh. Unlike cubic voxel representation employed by many popular competitors(*e.g. Minecraft*), Marching Cubes adheres more closely to the triangle-based visualization most commonly accepted by graphics languages and architecture. Visually, this results in smoother terrain and enables users to create softer shapes in their designs than would be otherwise possible. Voxel dimensions are 1/4th the user's height(although this is customizable) allowing for fine interaction without too many details to manage.

#### Snowy Tundra Biome, Split By a Cave
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Biomes.png) 

#### Cave Generation
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Cave.png)

### Expansive
  A distance based Level of Detail system is employed by Arterra to generate truly gargantuan terrains all at once. An octree based chunk design is used where chunks farther from the viewer are larger such that all chunks have the same detail but chunks are sparser farther away. As the viewer moves, the octree then adapts to merge/split chunks around their new position. With such a system, the viewer could theoretically generate terrain so large that floating point error becomes a concern. 

#### Large View of Terrain 
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Expanse.png)

#### Large Terrain Silhouettes
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Generation.png)

### Atmospherics
  Based on the paper [*Accurate Atmospheric Scattering*](https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering) by Sean O'Neil, atmospherics are calculated dynamically based on the viewer's position and orientation. However, due to the unique form of terrain representation, the density and scatter coefficients used within the algorithm aren't arbitrary values determined by just the viewer's orientation, but the **real world information** retrieved from generation. This means that clouds(and other atmospheric effects) can be generated through the same system used to generate materials, wherein a new (non-solid)material is placed in the sky which reflects white light. The benefit is that the atmosphere is interactive: a very real part of the world capable of being modified and revisualized.

#### Cloud Layering
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Atmosphere.png)

#### Raw Atmosphere
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/RawAtmosphere.png)

### Customizable
  All properties defining settings not intrinisc to the design of a system are exposed in one centralized *config* location. In-game, this config is procedurally serialized into UI which is exposed to the User such that all non-intrinsic settings are parametrized. This includes settings related to **biomes, structures, animals, noise, movement, interaction, etc.** which are made accessible for the user--meaning one is able to modify/create new features while playing not initially hardcoded by the developer. Modified settings are saved through a lazy strategy such that only modified portions of configs are saved for each instance.

#### Serialized Config UI and Structure Creation
<span style="display: flex; align-items: center;">
<img src="https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Config.png" alt="config" width="250"/>
<img src="https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Tree.png" alt="config" width="250"/>
</span> 

> [!NOTE]
> Example Config: \
> {"seed":1929656009,"_Quality":{"isDirty":false},"_Generation":{"isDirty":false},"_GamePlay":{"value":{"Terraforming":{"isDirty":false},"Movement":{"value":
> {"GroundFriction":0.05,"runSpeed":10.0,"jumpForce":8.0,"acceleration":50.0,"groundStickDist":0.05},"isDirty":true},"Crafting":{"isDirty":false},"Inventory":
> {"isDirty":false}},"isDirty":true},"_System":{"isDirty":false},"Quality":{"Atmosphere":{"isDirty":false},"Rendering":{"isDirty":false},"GeoShaders":{"isDirty":false},"Memory":
> {"isDirty":false}},"name":"Config(Clone)","hideFlags":0} 


## Interaction
  Distinctly, rather than merely describing the material at a certain location(i.e. this block is DIRT/STONE/AIR), Arterra reserves the ability to describe the **amount** of material at any position(i.e. there is 0.3 units of DIRT in this block). With this information, notably revolutionary/interesting interactions are possible, all while preserving the conservative nature of the system(no matter can be created/destroyed). For instance, **instead of the widespread mesh-based physics engine adopted by Unity, a simplified physics system is used based off just the raw world data** which avoids many issues with mesh updates and marching cube error. 

### Terraforming
  Terraforming refers to the way the user is able to modify our world-data thus changing the terrain. Traditionally, a viewer may do this by looking at the terrain feature they want to modify and either "Remove" or "Add" material to the terrain at that position through keybinds. For cubic voxel based terrains this is a discontinuous process, where many voxels are removed instantaneously(e.g. [lay of the land](https://store.steampowered.com/app/2776090/Lay_of_the_Land/)) or the user is delayed until a single voxel is removed(e.g. Minecraft). However, the benefit of describing the *amount* with every entry is that material can be removed in a continuous manner enabling smoother gameplay and finer fidelity in designs. 

#### Removing a Tree
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Terraform.png)

### Map Physics
  As a consequence of representing the *amount* of material at any position, several natural behaviors can be embodied faithfully through simple conservative modifications of this data. For instance, effects related to water physics are all quantifiable solely through operations on a single map entry(block) and its neighbors. In this case, the operations can be summarized concisely as,
  1. Move all water within an entry to the entry below it<sup>\*</sup>(as much as it can contain)
  2. Move all water above the entry into it<sup>\*</sup>
  3. Average out the remaining water in the entry with the other 4 neighboring entries of the same height<sup>\*</sup>\

  Due to how the map is represented, these set of 3 simple instructions can create water pools, waterfalls, and bubbles to form around air pockets underground that bubble to the surface. Moreover a similar yet slightly different set of operations can simulate gravity based physics, for cases like sand or gravel. 

#### Water Flowing Into a Cave
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Waterfall.png)
  
### Animals
  Custom AI, pathfinding, and BVHs are used to simulate animals which are capable of interacting with the terrain, each other, and the viewer. Pathfinding is done through a custom variation on the A<sup>*</sup> algorithm, where an animal's *profile* is used to determine whether a portion of the terrain is walkable. This profile not only describes the animal's size but by changing it it is also possible to have animals which can fly, walk, swim, or even travel underground(like worms). Unity's Job System is used to offload Animal simulations to a background process.

#### Animal Debug & Path Information
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Animals.png)

#### Rabbit Running
![](https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Rabbit.png)
*Models are customly made in Blender*

### Crafting
  Sometimes, a user may want to use materials not naturally found in the world, but a product of primary-secondary manufacturing processes. To account for this, a crafting process exists where the user can transform materials they possess(in their inventory) to different materials/items through placing them on a 4x4 grid. But inline with how the world is represented, the user doesn't place a single material at each grid corner but rather adds an *amount* of material continuously to the region over time. Put together, the viewer essentially **draws** their desired object, and the closest recipes are fitted to their design. 

#### Crafting SandStone
<img src="https://github.com/blackmagic919/Arterra/blob/main/Assets/Images/Recipe.png" alt="drawing" width="200"/> 

## Other Sources
<ins>For More Information About Implementation Refer Here:</ins>
- https://blackmagic919.github.io/AboutMe/ <-- Blog
- https://blackmagic919.github.io/Arterra/ <-- API Documentation
- https://deepwiki.com/blackmagic919/Arterra/ <-- Documentation by DeepWiki

