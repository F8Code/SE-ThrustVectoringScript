This is a thrust vectoring script for the game Space Engineers. <br />
This script allows flight using unidirectional thrust on a custom 360 degree rotating arm. <br />
It works by calculating the thrust vector which consists of direction and length which is passed onto the engine itself.  <br />
For now the project is considered WIP, as the use of subgrids in the design requires a gyroscopic stabilization script to counter inter-grid forces.  <br />
While I did implement said gyroscopic stabilization using PID controllers, I'd like to re-do it in a separate script, rather then have it contained in this one.  <br />
![Demo](vectoring1.gif)
![Demo](vectoring2.gif)
