import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Profile, SavedAddress } from '../../core/models';
import { SeoService } from '../../core/seo.service';
import { ToastService } from '../../core/toast.service';
import { AccountNavComponent } from '../../shared/account-nav.component';

const PHONE = /^(?:\+?88)?01[3-9]\d{8}$/;
const DIVISIONS = ['Barishal', 'Chattogram', 'Dhaka', 'Khulna', 'Mymensingh', 'Rajshahi', 'Rangpur', 'Sylhet'];

interface AddressForm {
  id: number;
  name: string;
  phone: string;
  division: string;
  district: string;
  thana: string;
  line: string;
  isDefault: boolean;
}

const blankAddress = (profile?: Profile | null): AddressForm => ({
  id: 0,
  name: profile?.name ?? '',
  phone: profile?.phone ?? '',
  division: 'Rangpur',
  district: '',
  thana: '',
  line: '',
  isDefault: false
});

/** /account/profile — details, password, phone verification and saved addresses. */
@Component({
  selector: 'rt-account-settings',
  standalone: true,
  imports: [FormsModule, AccountNavComponent],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Your account</div>
      <h1 class="mb-0">Profile &amp; addresses</h1>
      <rt-account-nav />

      @if (profile(); as p) {
        <div class="rt-grid rt-cols-2" style="gap:20px; align-items:start;">
          <!-- PROFILE -->
          <form class="rt-panel" (ngSubmit)="saveProfile()">
            <h3 class="mt-0">Your details</h3>

            <div class="rt-field">
              <label class="rt-label" for="pf-name">Full name</label>
              <input id="pf-name" name="name" class="rt-input" [(ngModel)]="name" required maxlength="120" autocomplete="name" />
            </div>

            <div class="rt-field">
              <label class="rt-label" for="pf-phone">Mobile number</label>
              <input id="pf-phone" class="rt-input" [value]="p.phone" disabled />
              <p class="tiny muted mb-0">
                @if (p.phoneVerified) {
                  ✓ Verified.
                } @else {
                  Not verified yet.
                }
                Your number is your sign-in, so it can't be changed here.
              </p>
            </div>

            <div class="rt-field">
              <label class="rt-label" for="pf-email">Email (optional)</label>
              <input id="pf-email" name="email" type="email" class="rt-input" [(ngModel)]="email" maxlength="254" autocomplete="email" />
            </div>

            <button class="rt-btn rt-btn--sm" type="submit" [disabled]="savingProfile() || !name.trim()">
              {{ savingProfile() ? 'Saving…' : 'Save details' }}
            </button>
          </form>

          <div class="rt-stack" style="gap:20px;">
            <!-- VERIFY PHONE -->
            @if (!p.phoneVerified) {
              <form class="rt-panel" (ngSubmit)="verifyPhone()">
                <h3 class="mt-0">Verify your phone</h3>
                <p class="small">We'll text a short code to {{ p.phone }}.</p>

                @if (!codeSent()) {
                  <button class="rt-btn rt-btn--sm" type="button" [disabled]="sendingCode()" (click)="sendCode()">
                    {{ sendingCode() ? 'Sending…' : 'Send code' }}
                  </button>
                } @else {
                  <div class="rt-field">
                    <label class="rt-label" for="pf-code">6-digit code</label>
                    <input id="pf-code" name="code" class="rt-input" inputmode="numeric" maxlength="6" [(ngModel)]="code" autocomplete="one-time-code" />
                  </div>
                  <button class="rt-btn rt-btn--sm" type="submit" [disabled]="code.length < 4">Verify</button>
                  <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" style="margin-left:8px;" (click)="sendCode()">Resend</button>
                }
              </form>
            }

            <!-- PASSWORD -->
            <form class="rt-panel" (ngSubmit)="changePassword()">
              <h3 class="mt-0">Change password</h3>

              <div class="rt-field">
                <label class="rt-label" for="pw-current">Current password</label>
                <input id="pw-current" name="current" type="password" class="rt-input" [(ngModel)]="currentPassword" autocomplete="current-password" />
              </div>
              <div class="rt-field">
                <label class="rt-label" for="pw-new">New password (8+ characters)</label>
                <input id="pw-new" name="next" type="password" class="rt-input" [(ngModel)]="newPassword" autocomplete="new-password" minlength="8" />
              </div>

              <button class="rt-btn rt-btn--sm" type="submit" [disabled]="savingPassword() || !currentPassword || newPassword.length < 8">
                {{ savingPassword() ? 'Saving…' : 'Change password' }}
              </button>
            </form>
          </div>
        </div>

        <!-- ADDRESSES -->
        <div class="rt-row rt-row--between rt-row--wrap" style="margin-top:28px;">
          <h2 class="mb-0">Saved addresses</h2>
          <button class="rt-btn rt-btn--sm" type="button" (click)="newAddress()">+ Add address</button>
        </div>

        @if (form(); as f) {
          <form class="rt-panel mt-2" (ngSubmit)="saveAddress()">
            <h3 class="mt-0">{{ f.id ? 'Edit address' : 'New address' }}</h3>

            <div class="rt-grid rt-cols-2" style="gap:12px;">
              <div class="rt-field">
                <label class="rt-label" for="ad-name">Receiver name</label>
                <input id="ad-name" name="aname" class="rt-input" [(ngModel)]="f.name" required maxlength="120" />
              </div>
              <div class="rt-field">
                <label class="rt-label" for="ad-phone">Mobile number</label>
                <input id="ad-phone" name="aphone" class="rt-input" inputmode="tel" [(ngModel)]="f.phone" required />
                @if (f.phone && !validPhone(f.phone)) {
                  <p class="rt-error mb-0">Enter a Bangladeshi mobile number, e.g. 01712345678.</p>
                }
              </div>
              <div class="rt-field">
                <label class="rt-label" for="ad-div">Division</label>
                <select id="ad-div" name="adiv" class="rt-select" [(ngModel)]="f.division">
                  @for (d of divisions; track d) {
                    <option [value]="d">{{ d }}</option>
                  }
                </select>
              </div>
              <div class="rt-field">
                <label class="rt-label" for="ad-dist">District</label>
                <input id="ad-dist" name="adist" class="rt-input" [(ngModel)]="f.district" required maxlength="80" />
              </div>
              <div class="rt-field">
                <label class="rt-label" for="ad-thana">Thana / area</label>
                <input id="ad-thana" name="athana" class="rt-input" [(ngModel)]="f.thana" required maxlength="80" />
              </div>
              <div class="rt-field">
                <label class="rt-label" for="ad-line">House, road, village</label>
                <input id="ad-line" name="aline" class="rt-input" [(ngModel)]="f.line" required maxlength="200" />
              </div>
            </div>

            <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
              <input type="checkbox" name="adefault" [(ngModel)]="f.isDefault" /> Use as my default address
            </label>

            <div class="rt-row" style="gap:8px;">
              <button class="rt-btn rt-btn--sm" type="submit" [disabled]="savingAddress() || !addressValid(f)">
                {{ savingAddress() ? 'Saving…' : 'Save address' }}
              </button>
              <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
            </div>
          </form>
        }

        @if (addresses().length === 0 && !form()) {
          <div class="rt-panel center mt-2" style="padding:28px;">
            <p class="mb-0">No saved addresses yet. Add one to check out faster.</p>
          </div>
        } @else {
          <div class="rt-grid rt-cols-2 mt-2" style="gap:14px;">
            @for (a of addresses(); track a.id) {
              <div class="rt-panel">
                <div class="rt-row rt-row--between">
                  <strong>{{ a.name }}</strong>
                  @if (a.isDefault) {
                    <span class="rt-tag rt-tag--teal">Default</span>
                  }
                </div>
                <p class="small mb-0">{{ a.line }}, {{ a.thana }}, {{ a.district }}, {{ a.division }}</p>
                <p class="small muted">{{ a.phone }}</p>
                <div class="rt-row" style="gap:6px;">
                  <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="editAddress(a)">Edit</button>
                  <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="removeAddress(a)">Remove</button>
                </div>
              </div>
            }
          </div>
        }
      } @else if (loading()) {
        <div class="rt-skeleton" style="height:260px;"></div>
      }
    </div>
  `
})
export class AccountSettingsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly seo = inject(SeoService);

  readonly divisions = DIVISIONS;
  readonly profile = signal<Profile | null>(null);
  readonly addresses = signal<SavedAddress[]>([]);
  readonly loading = signal(true);
  readonly savingProfile = signal(false);
  readonly savingPassword = signal(false);
  readonly savingAddress = signal(false);
  readonly sendingCode = signal(false);
  readonly codeSent = signal(false);
  readonly form = signal<AddressForm | null>(null);

  name = '';
  email = '';
  currentPassword = '';
  newPassword = '';
  code = '';

  ngOnInit(): void {
    this.seo.set({ title: 'Your profile', noindex: true });
    this.loadProfile();
    this.loadAddresses();
  }

  validPhone(value: string): boolean {
    return PHONE.test(value.trim());
  }

  addressValid(f: AddressForm): boolean {
    return !!(f.name.trim() && this.validPhone(f.phone) && f.district.trim() && f.thana.trim() && f.line.trim());
  }

  // ---- profile

  private loadProfile(): void {
    this.api.get<Profile>('/me').subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.name = profile.name;
        this.email = profile.email ?? '';
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  saveProfile(): void {
    if (this.savingProfile()) {
      return;
    }

    this.savingProfile.set(true);

    this.api.put<Profile>('/me', { name: this.name.trim(), email: this.email.trim() || null }).subscribe({
      next: (profile) => {
        this.savingProfile.set(false);
        this.profile.set(profile);
        // Keep the header and session in step with the saved name.
        const user = this.auth.user();
        if (user) {
          this.auth.user.set({ ...user, name: profile.name, email: profile.email });
        }
        this.toast.show('Details saved.');
      },
      error: (error: unknown) => {
        this.savingProfile.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  changePassword(): void {
    if (this.savingPassword()) {
      return;
    }

    this.savingPassword.set(true);

    this.api.post('/me/password', { currentPassword: this.currentPassword, newPassword: this.newPassword }).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.currentPassword = '';
        this.newPassword = '';
        this.toast.show('Password changed.');
      },
      error: (error: unknown) => {
        this.savingPassword.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  // ---- phone verification

  sendCode(): void {
    const phone = this.profile()?.phone;
    if (!phone || this.sendingCode()) {
      return;
    }

    this.sendingCode.set(true);

    this.api.post('/auth/otp/send', { phone, purpose: 'verify' }).subscribe({
      next: () => {
        this.sendingCode.set(false);
        this.codeSent.set(true);
        this.toast.show('Code sent. It expires in a few minutes.');
      },
      error: (error: unknown) => {
        this.sendingCode.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  verifyPhone(): void {
    const phone = this.profile()?.phone;
    if (!phone) {
      return;
    }

    this.api.post('/auth/otp/verify', { phone, code: this.code.trim(), purpose: 'verify' }).subscribe({
      next: () => {
        this.toast.show('Phone verified.');
        this.codeSent.set(false);
        this.code = '';
        this.loadProfile();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  // ---- addresses

  private loadAddresses(): void {
    this.api.get<SavedAddress[]>('/me/addresses').subscribe({
      next: (items) => this.addresses.set(items ?? []),
      error: () => this.addresses.set([])
    });
  }

  newAddress(): void {
    this.form.set({ ...blankAddress(this.profile()), isDefault: this.addresses().length === 0 });
  }

  editAddress(a: SavedAddress): void {
    this.form.set({ ...a });
  }

  saveAddress(): void {
    const f = this.form();
    if (!f || this.savingAddress() || !this.addressValid(f)) {
      return;
    }

    const body = {
      name: f.name.trim(),
      phone: f.phone.trim(),
      division: f.division,
      district: f.district.trim(),
      thana: f.thana.trim(),
      line: f.line.trim(),
      isDefault: f.isDefault
    };

    this.savingAddress.set(true);
    const request = f.id ? this.api.put(`/me/addresses/${f.id}`, body) : this.api.post('/me/addresses', body);

    request.subscribe({
      next: () => {
        this.savingAddress.set(false);
        this.form.set(null);
        this.toast.show('Address saved.');
        this.loadAddresses();
      },
      error: (error: unknown) => {
        this.savingAddress.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  removeAddress(a: SavedAddress): void {
    if (typeof window !== 'undefined' && !window.confirm('Remove this address?')) {
      return;
    }

    this.api.delete(`/me/addresses/${a.id}`).subscribe({
      next: () => {
        this.toast.show('Address removed.');
        this.loadAddresses();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
