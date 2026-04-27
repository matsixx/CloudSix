**CloudSix**

This is a total replacement of Tarkov's built in clouds.

Support my work on Ko-fi: https://ko-fi.com/matsix

After over analyzing my clouds in VR, I wrote a custom over engineered volumetric cloud shader that works in VR and flatscreen. ~~I went to test something on flatscreen and it kinda hit me how low res the clouds looked. So, I just added the clouds on flatscreen and it looks pretty good on there also. This is still a work in progress, no configuration for now because of the sky changing frame by frame. Shouldn't be hard to add at some point though.~~

These clouds wouldn't have been possible without the paper from Guerrilla Games on how they did the clouds in Horizon Zero Dawn - https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn

My main focus with these clouds was keeping them visually stable and very optimized because I wanted them to work well in VR too. They could still use some improving, a lot of improving can be done just with the values I locked behind in advanced settings. You can also increase the primary steps/light steps higher by going into advanced. By default, this runs at half res, 64 primary steps on clouds and 16 light steps. I don't believe there's a big performance impact here unless you're on older hardware.

**Features:**
- Weather affects density
- Wind affects cloud speed
- Physically based volumetrics aware of sun/moon position
- Procedural coloring based on Tarkov's atmospherics
- Well optimized - it performs well in VR also
- Terrain shadows based on cloud position

