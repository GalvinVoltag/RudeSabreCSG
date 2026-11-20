# AI Disclosure
Most of the modifications to this fork are made by Claude and Rool, not by me. I am in the process of turning this project into a legit one by rewriting said sections.

This was because I wanted to see if it was possible to make it work and alas, it is. If you work around the bugs and export your map, it will compile into a playable map.

Give me some time to read through and understand how Sabre works in order to port it fully for Rude.

# RudeSabreCSG

RudeSabreCSG is a set of level design tools for building complex levels quickly inside Rude Level Editor based on SabreCSG.

Using Constructive Solid Geometry techniques SabreCSG allows you to add and subtract brushes to create great levels without needing to understand 3D art packages.

| | |
| --- | --- |
| [![Introduction Image 1](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-1.png)](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-1.png) | **ADD AND SUBTRACT BRUSHES TOGETHER TO MAKE GREAT PLAY SPACES** <p>Boolean brushes allow you to add and subtract arbitrary shapes together to rapidly create walls, rooms or carve out doors and windows. The brushes are used as a template for building the final geometry allowing you unprecedented flexibility for making minor tweaks or big changes simply by modifying the brushes.</p> |
| **POWERFUL BRUSH AND GEOMETRY TOOLS** <p>SabreCSG comes equipped with a whole range of tools for editing brushes and geometry, from intuitive resize tools which let you quickly block out spaces to vertex and edge editing modes which let you go in and tweak in more detail, to advanced clipping tools allowing you to bevel or cut entire brushes in two.</p> | [![Introduction Image 2](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-2.png)](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-2.png) |
| [![Introduction Image 3](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-3.png)](https://github.com/sabresaurus/SabreCSG/wiki/images/home-introduction-3.png) | **APPLY MATERIALS AND EDIT UVs** <p>With its surface tools, SabreCSG allows you to translate, rotate and scale surfaces, apply new materials, exclude faces from the final geometry for speed. And with newly upgraded features there’s a whole set of new helper functions to speed up your workflow.</p> |

SabreCSG was originally sold on the [Unity Asset Store](https://www.assetstore.unity3d.com/en/#!/content/47418), but as of 14th October 2016 has been [open sourced](http://sabrecsg.com/2016/10/14/sabrecsg-released-as-open-source/) under the MIT License (see LICENSE for details). It is now maintained on this [Github repository](https://github.com/sabresaurus/SabreCSG).

## Core Features:
(ones starting with a + are extra to by RudeSabreCSG)

- Boolean CSG algorithm supporting partial rebuilding.
- +Subdivide resulting mesh into grids for vertex lighting to work.
- Draw tools to rapidly create levels by drawing brushes directly.
- Resize tools allow you to resize spaces intuitively.
- +Chose between splitting resulting mesh per material, per brush, or per group.
- +Have multiple CSGModel instances in the same scene for having multiple rooms.
- Vertex tools to refine and add precision details.
- Change materials or edit UVs with surface tools built for level design.
- Clip planes allow you to bevel brushes or even split them in two.
- Export built geometry to .OBJ for use in external 3D tools.
- Vertex Colors - Apply vertex colors per face which are baked into the built meshes.
- Experimental Code API - Experimental support for procedurally generating levels through code.

Documentation and videos on how to use SabreCSG are available at the [SabreCSG Learn website](http://sabrecsg.com/learn/) or the [wiki](https://github.com/sabresaurus/SabreCSG/wiki).

Notes for how to get around certain bugs:
- PerBrush and PerGroup splitting modes can cause bugs with UV and Face tools. I recommend setting it to PerMaterial during edit, and turning it on before export.
- SubdivideVisualMesh breaks Face editor, preventing changing materials and UVs. Disable this dueing edit.
- UI works sometimes, and doesn't in other times. If you can't click on any gizmos, then just move the camera a little with right click, or change your tool to Rotate or View.
- The editor camera gets stuck sometimes. Changing Sabre tool to Face seems to relieve this bug at some times.

## Download

[![Download SabreCSG](https://github.com/sabresaurus/SabreCSG/wiki/images/home/home-button-1.png)](https://github.com/sabresaurus/SabreCSG/archive/master.zip)

[![Installation Instructions](https://github.com/sabresaurus/SabreCSG/wiki/images/home/home-button-4.png)](https://youtu.be/KDheSZcagsw?t=30)

## Documentation

[![Visit Wiki Pages](https://github.com/sabresaurus/SabreCSG/wiki/images/home/home-button-3.png)](https://github.com/sabresaurus/SabreCSG/wiki)

[![Join the Official SabreCSG Discord Server](https://github.com/sabresaurus/SabreCSG/wiki/images/home/home-button-2.png)](https://discord.gg/pFAS5rK)

## Donations:

If you love SabreCSG and wish to say thanks then please feel free to make a donation. Your donations are a huge motivator to continue the development of SabreCSG.

[![paypal](https://www.paypalobjects.com/en_US/i/btn/btn_donateCC_LG.gif)](https://paypal.me/henrydejongh)
