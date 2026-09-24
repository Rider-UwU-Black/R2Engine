# Make a Door Between Scenes

A scene door combines an interaction area with a destination scene and spawn point.

## Prepare the destination

1. Open the scene the door should lead to.
2. Create an empty object at the arrival position.
3. Give it a unique destination name that explains where it belongs, such as Hall From Bedroom.
4. Save the destination scene.

## Configure the door

1. Return to the scene containing the door.
2. Select the door and add the reusable scene-loading interaction script.
3. Choose the destination scene.
4. Enter the destination object's exact name.
5. Add or adjust the trigger collider in front of the door.
6. Assign an optional prompt Canvas if you want an authored Press Cross or Enter prompt.

Test the trip in both directions. If the new scene loads but the player appears in the wrong place, check the destination name for spelling and make sure it is unique.

Use a destination rather than permanently changing the scene's default spawn. That lets several doors enter the same scene at different locations.
