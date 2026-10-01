import { Component, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="padding: 2rem; font-family: sans-serif;">
      <h2>Завдання: Request / Response</h2>
      <button (click)="fetchData()" style="padding: 10px 20px; font-size: 16px; cursor: pointer;">
        Відправити Request на .NET сервер
      </button>
      
      <div *ngIf="response" style="margin-top: 20px; padding: 15px; background: #f4f4f4; border-radius: 5px;">
        <h3>Отриманий Response:</h3>
        <pre>{{ response | json }}</pre>
      </div>
    </div>
  `
})
export class AppComponent {

  private apiUrl = 'https://localhost:7087/api/Test'; 
  
  private http = inject(HttpClient);
  response: any = null;

  fetchData() {
    this.http.get(this.apiUrl).subscribe({
      next: (res) => {
        this.response = res;
        console.log('Дані отримано успішно', res);
      },
      error: (err) => {
        console.error('Помилка при виконанні запиту', err);
      }
    });
  }
}