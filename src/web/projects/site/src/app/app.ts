import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BackToTop } from './shared/layout/back-to-top/back-to-top';
import { Footer } from './shared/layout/footer/footer';
import { Header } from './shared/layout/header/header';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Header, Footer, BackToTop],
  templateUrl: './app.html',
})
export class App {}
