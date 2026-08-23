# Sized Apparel Auto-Refit

Generates the sized apparel textures Sized Apparel for RJW expects, for garments it has no
hand-drawn variants for.

Overall this is mostly here for anyone who wants to turn this on for clothing that work with the drawn parts without needing resizing, or allow for very minor resizing in a few spots for unsupported apparel.
This is meant to appeal to anyone who wants to use the custom drawn apparel, only a few of its features, or simply just wants some granular control on a few pieces.

This resizing is still a bit rough but i feel like its "alright" enough to start testing.

## The refit

It just does the breasts nothing else right now. Mostly tries to match at the widest point. This part is not perfect but should cover if you have it on, robes work best with it as they're already a bit wide 
in texture so its morph is gonna be the least agressive.

## Settings

New main menu bar button to bring up the menu, alonng with a few more configs for the mod menu configs (mostly cache related and defaults).
Most of this is in the refit menu itself, if a pawn is selected you get a preview window if not you get the full list of garments in your game, with sliders and checkboxes.
This only pulls apparel without sized apparel entries already beacuse we don't need to break how those work.

One checkbox to actually draw the breasts and one to do the resize. Even if you don't wanna use the resize some clothing pieces leave the chest exposed for example and would look more consistent if it drew the chest. Being that SA0
only has an global on/off switch this gives you that granular control for each piece of clothing (and arguably each bodytype).

Otherwise the menu allows for per-bodytype adjustments, if you're on the preview menu you can change it for other bodytypes but its not gonna morph that pawn so i suggest using pawn editor to change their body if you wanna test 
every type of body.

## Performance

Def write and otherwise cached images. I'm not re-morphing things on the frame/tick so once you exit that refit menu the Performance impact should become 0 as no background tasks run.
This basically hands off everything to SA0 to handle in regards to draw/display once we just make the morphs.

From a behind-the scenes perspective SA0 has one piece of clothing for each "severity" level of the breasts headiff and ties that to a bodypart + clothing size. This just generates one for each tier and leaves it be.
