import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
  kind: 'info' | 'error';
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private sequence = 0;
  readonly toasts = signal<Toast[]>([]);

  show(message: string, kind: 'info' | 'error' = 'info'): void {
    const id = ++this.sequence;
    this.toasts.update((list) => [...list, { id, message, kind }]);

    setTimeout(() => this.dismiss(id), kind === 'error' ? 6000 : 3500);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((toast) => toast.id !== id));
  }
}
