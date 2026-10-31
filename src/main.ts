import './style.css';
import { Game } from './core/Game';

const canvas = document.getElementById('scene') as HTMLCanvasElement;
const ui = document.getElementById('ui') as HTMLElement;

// let the loading screen reach the screen before the heavy work of building the world begins
setTimeout(() => void new Game(canvas, ui).start(), 50);
