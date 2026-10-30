import './style.css';
import { Game } from './core/Game';

const canvas = document.getElementById('scene') as HTMLCanvasElement;
const ui = document.getElementById('ui') as HTMLElement;

new Game(canvas, ui).start();
