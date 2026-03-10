<template>
  <div class="profile-layout">
    <div class="profile-header">
      <div class="user-profile">
        <div class="user-avatar">
          <img v-if="imageUrl" :src="imageUrl" class="avatar-img" alt="Profile" />
          <span v-else>{{ userInitial }}</span>
        </div>
        <div class="user-meta">
          <h1 class="user-name">{{ name || 'User Profile' }}</h1>
        </div>
      </div>
    </div>

    <div class="settings-grid">
      <!-- Identity & Communication -->
      <section class="settings-card">
        <div class="card-header">
          <div class="card-header-icon">🛡️</div>
          <div>
            <h3>Identity & Communication</h3>
            <p>Manage your phone number and alert preferences.</p>
          </div>
        </div>
        
        <div class="identity-info" v-if="!editMode">
          <div class="info-row">
            <span class="info-label">Phone Number</span>
            <span class="info-value">{{ formatPhone(phone) }}</span>
          </div>
          <div class="info-row">
            <span class="info-label">Channel</span>
            <span class="info-value badge channel">{{ preferredChannel }}</span>
          </div>
          <button class="action-btn secondary btn-sm" @click="enterEditMode">Edit Details</button>
        </div>

        <div class="identity-edit" v-else>
          <div class="control-group">
            <label class="compact-label">Phone Number</label>
            <input type="text" v-model="tempPhone" class="standard-input" placeholder="+1234567890" :disabled="isVerifying" />
          </div>
          
          <div class="control-group">
            <label class="compact-label">Communication Channel</label>
            <div class="tab-group">
              <button 
                v-for="ch in ['SMS', 'WhatsApp']" 
                :key="ch"
                :class="{ active: tempChannel === ch }"
                @click="tempChannel = ch"
                :disabled="isVerifying"
              >
                {{ ch }}
              </button>
            </div>
          </div>

          <div v-if="!isVerifying" class="edit-actions">
            <button class="action-btn primary" @click="startVerification" :disabled="isSendingOtp">
              {{ isSendingOtp ? 'Sending code...' : 'Verify & Save' }}
            </button>
            <button class="action-btn secondary" @click="cancelEdit">Cancel</button>
          </div>

          <div v-else class="verification-flow">
            <div class="otp-box">
              <p class="verification-hint">Enter the 6-digit code sent to {{ tempPhone }}</p>
              <input type="text" v-model="otpCode" class="otp-input" maxlength="6" placeholder="000000" />
            </div>
            <div class="edit-actions">
              <button class="action-btn primary" @click="confirmVerification" :disabled="isLoading">
                {{ isLoading ? 'Verifying...' : 'Confirm' }}
              </button>
              <button class="action-btn secondary" @click="isVerifying = false">Back</button>
            </div>
          </div>
        </div>
      </section>

      <!-- Reporting Control -->
      <section class="settings-card">
        <div class="card-header">
          <div class="card-header-icon">📊</div>
          <div>
            <h3>Report Schedule</h3>
            <p>Set how often you want to receive cloud health reports.</p>
          </div>
        </div>
        
        <div class="control-group">
          <label class="compact-label">Frequency Preference</label>
          <div class="tab-group">
            <button 
              v-for="freq in frequencies" 
              :key="freq"
              :class="{ active: schedule.frequency === freq }"
              @click="schedule.frequency = freq"
            >
              {{ freq }}
            </button>
          </div>
        </div>

        <div class="toggle-stack">
          <div class="toggle-item">
            <div class="toggle-info">
              <span class="toggle-title">Cost Optimization</span>
              <span class="toggle-hint">Detect financial leaks in real-time</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="schedule.includeCostAnalysis" />
              <span class="slider"></span>
            </label>
          </div>
          
          <div class="toggle-item">
            <div class="toggle-info">
              <span class="toggle-title">Security Posture</span>
              <span class="toggle-hint">Audit risks and compliance gaps</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="schedule.includeSecurityHealth" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-item">
            <div class="toggle-info">
              <span class="toggle-title">Monitoring active</span>
              <span class="toggle-hint">Enable background cloud monitoring</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="schedule.isActive" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <button class="action-btn primary" @click="saveSchedule" :disabled="isLoading">
          <span v-if="isLoading">Saving settings...</span>
          <span v-else>Update Schedule</span>
        </button>
      </section>

      <!-- Connection Manager -->
      <section class="settings-card">
        <div class="card-header">
          <div class="card-header-icon">🔗</div>
          <div>
            <h3>Cloud Accounts</h3>
            <p>Your connected cloud environments and resources.</p>
          </div>
        </div>

        <div class="cloud-stack">
          <div v-for="cloud in connections" :key="cloud.subscriptionId" class="cloud-row">
            <div class="provider-icon" :class="cloud.provider.toLowerCase()"></div>
            <div class="cloud-info">
              <span class="provider-name">{{ cloud.provider }}</span>
              <span class="subscription-id">{{ cloud.subscriptionId }}</span>
            </div>
            <span class="status-indicator online">Online</span>
          </div>
          
          <div v-if="connections.length === 0" class="empty-state">
            No cloud accounts connected yet.
          </div>
        </div>
        
        <button class="action-btn secondary" @click="goToSetup">Connect New Provider</button>
      </section>
    </div>

    <!-- Notification Overlay -->
    <Transition name="slide-up">
      <div v-if="notification" class="alert-banner" :class="notification.type">
        <span class="alert-icon">{{ notification.type === 'success' ? '✓' : '⚠️' }}</span>
        {{ notification.message }}
      </div>
    </Transition>
  </div>
</template>

<script setup>
import { ref, onMounted, computed } from 'vue';
import { authClient } from '../lib/auth';
import { dracoApiFetch } from '../lib/dracoApi';

const phone = ref('');
const name = ref('');
const connections = ref([]);
const isLoading = ref(false);
const notification = ref(null);
const imageUrl = ref(null);
const preferredChannel = ref('SMS');
const authId = ref('');

// Editing State
const editMode = ref(false);
const isVerifying = ref(false);
const isSendingOtp = ref(false);
const tempPhone = ref('');
const tempChannel = ref('SMS');
const otpCode = ref('');

const frequencies = ['Daily', 'Weekly', 'Monthly'];

const schedule = ref({
  frequency: 'Weekly',
  includeCostAnalysis: true,
  includeSecurityHealth: true,
  isActive: true
});

const userInitial = computed(() => name.value ? name.value.charAt(0).toUpperCase() : '👤');

const formatPhone = (p) => {
  if (!p) return 'No Phone Number';
  return p.replace(/(\+\d{1})(\d{3})(\d{3})(\d{4})/, '$1 ($2) $3-$4');
};

const fetchProfile = async () => {
  try {
    const { data: sessionData } = await authClient.getSession();
    
    if (!sessionData || !sessionData.user) {
      console.warn("[ProfileSettings] No valid session found.");
      window.location.href = '/login';
      return;
    }

    // Set temporary name/initial from session while we wait for Draco API
    name.value = sessionData.user.name || sessionData.user.email || 'User';

    try {
      const res = await dracoApiFetch('/api/auth/me');
      
      if (res.status === 404) {
        console.log("[ProfileSettings] No Draco account - redirecting to /setup");
        window.location.href = '/setup';
        return;
      }

      if (res.ok) {
          const data = await res.json();
          name.value = data.name || name.value;
          phone.value = data.phone;
          imageUrl.value = data.imageUrl;
          preferredChannel.value = data.preferredChannel || 'SMS';
          authId.value = data.authId || '';
          connections.value = data.connections || [];
          if (data.schedule) {
            schedule.value = {
                frequency: data.schedule.frequency,
                includeCostAnalysis: data.schedule.includeCostAnalysis,
                includeSecurityHealth: data.schedule.includeSecurityHealth,
                isActive: data.schedule.isActive
            };
          }
      } else {
        const errText = await res.text();
        console.error("[ProfileSettings] Draco API error:", res.status, errText);
        showNotification(`API Error (${res.status}): ${errText.substring(0, 50)}`, 'error');
      }
    } catch (apiErr) {
      console.error("[ProfileSettings] dracoApiFetch failed:", apiErr);
      showNotification("Connect to API failed. Checkout the Console.", "error");
    }
  } catch (err) {
    console.error("Failed to fetch profile settings", err);
    showNotification("Auth session error.", "error");
  } finally {
    isLoading.value = false;
  }
};

const saveSchedule = async () => {
  isLoading.value = true;
  try {
    const res = await dracoApiFetch('/api/reports/schedule', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        phone: phone.value,
        ...schedule.value
      })
    });

    if (res.ok) {
      showNotification('Settings saved successfully.', 'success');
    } else {
      throw new Error('Save failed');
    }
  } catch (err) {
    showNotification('Failed to save settings.', 'error');
  } finally {
    isLoading.value = false;
  }
};

const showNotification = (msg, type) => {
  notification.value = { message: msg, type };
  setTimeout(() => notification.value = null, 4000);
};

const goToSetup = () => {
  window.location.href = '/setup';
};

const enterEditMode = () => {
  tempPhone.value = phone.value;
  tempChannel.value = preferredChannel.value;
  editMode.value = true;
};

const cancelEdit = () => {
  editMode.value = false;
  isVerifying.value = false;
  otpCode.value = '';
};

const startVerification = async () => {
  if (!tempPhone.value) {
    showNotification("Phone number is required.", "error");
    return;
  }

  isSendingOtp.value = true;
  try {
    const res = await dracoApiFetch('/api/auth/verify-phone', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        phone: tempPhone.value,
        channel: tempChannel.value
      })
    });

    if (res.ok) {
      isVerifying.value = true;
      showNotification("Verification code sent!", "success");
    } else {
      const err = await res.text();
      showNotification(err || "Failed to send code.", "error");
    }
  } catch (err) {
    showNotification("Error connecting to verification service.", "error");
  } finally {
    isSendingOtp.value = false;
  }
};

const confirmVerification = async () => {
  if (otpCode.value.length < 6) {
    showNotification("Please enter the 6-digit code.", "error");
    return;
  }

  isLoading.value = true;
  try {
    const res = await dracoApiFetch('/api/auth/confirm-phone', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        newPhone: tempPhone.value,
        code: otpCode.value,
        preferredChannel: tempChannel.value
      })
    });

    if (res.ok) {
      phone.value = tempPhone.value;
      preferredChannel.value = tempChannel.value;
      editMode.value = false;
      isVerifying.value = false;
      otpCode.value = '';
      showNotification("Profile updated successfully!", "success");
    } else {
      const data = await res.json();
      showNotification(data.message || "Verification failed.", "error");
    }
  } catch (err) {
    showNotification("Error confirming verification.", "error");
  } finally {
    isLoading.value = false;
  }
};

onMounted(async () => {
  isLoading.value = true;
  await fetchProfile();
});
</script>

<style scoped>
.profile-layout {
  width: 100%;
  max-width: 1100px;
  margin: 0 auto;
  min-height: 400px;
  animation: pageReveal 0.6s cubic-bezier(0.16, 1, 0.3, 1);
}
/* ... existing styles ... */

.profile-header {
  margin-bottom: 3.5rem;
  padding-bottom: 2rem;
  border-bottom: 1px solid var(--border-color);
}

.user-profile {
  display: flex;
  align-items: center;
  gap: 2rem;
}

.user-avatar {
  width: 90px;
  height: 90px;
  background: linear-gradient(135deg, var(--dragon-red) 0%, #7c0d11 100%);
  border-radius: var(--radius-lg);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2.5rem;
  font-weight: 800;
  color: white;
  box-shadow: 0 10px 25px rgba(177, 17, 22, 0.25);
  border: 1px solid rgba(255, 255, 255, 0.1);
  overflow: hidden;
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.user-name {
  font-size: 3rem;
  font-weight: 900;
  margin: 0;
  letter-spacing: -1.5px;
  line-height: 1;
}

.user-badges {
  display: flex;
  gap: 0.75rem;
  margin-top: 1rem;
}

.badge {
  padding: 0.35rem 0.75rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.badge.phone {
  background: rgba(255, 255, 255, 0.05);
  color: var(--sub-text-color);
  border: 1px solid var(--border-color);
  font-family: monospace;
}

.settings-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2.5rem;
}

.settings-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 2.5rem;
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.card-header {
  display: flex;
  gap: 1.25rem;
  align-items: flex-start;
}

.card-header-icon {
  font-size: 1.5rem;
  background: rgba(255, 255, 255, 0.05);
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
}

.card-header h3 {
  font-size: 1.25rem;
  font-weight: 800;
  margin: 0;
}

.card-header p {
  font-size: 0.85rem;
  color: var(--sub-text-color);
  margin-top: 0.25rem;
}

.compact-label {
  display: block;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  color: #64748b;
  margin-bottom: 0.75rem;
  letter-spacing: 1px;
}

.tab-group {
  display: flex;
  background: #0f172a;
  padding: 0.3rem;
  border-radius: 8px;
  border: 1px solid var(--border-color);
}

.tab-group button {
  flex: 1;
  padding: 0.65rem;
  background: transparent;
  border: none;
  color: #64748b;
  font-weight: 600;
  font-size: 0.85rem;
  cursor: pointer;
  border-radius: 6px;
  transition: all 0.2s;
}

.tab-group button.active {
  background: var(--dragon-red);
  color: white;
}

.toggle-stack {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.toggle-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.03);
}

.toggle-title {
  display: block;
  font-weight: 700;
  font-size: 0.95rem;
}

.toggle-hint {
  display: block;
  font-size: 0.8rem;
  color: #64748b;
  margin-top: 0.15rem;
}

/* Switches */
.switch { position: relative; display: inline-block; width: 44px; height: 24px; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
  background-color: #334155; transition: .4s; border-radius: 24px;
}
.slider:before {
  position: absolute; content: ""; height: 18px; width: 18px; left: 3px; bottom: 3px;
  background-color: white; transition: .4s; border-radius: 50%;
}
input:checked + .slider { background-color: var(--dragon-red); }
input:checked + .slider:before { transform: translateX(20px); }

.action-btn {
  padding: 1.1rem;
  border-radius: 8px;
  font-weight: 800;
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.25s;
}

.action-btn.primary {
  background: var(--dragon-red);
  color: white;
  border: 1px solid #7c0d11;
  box-shadow: 0 4px 15px rgba(177, 17, 22, 0.2);
}

.action-btn.primary:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 8px 25px rgba(177, 17, 22, 0.35);
}

.action-btn.secondary {
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text-color);
}

.action-btn.secondary:hover {
  border-color: var(--dragon-red);
  background: rgba(177, 17, 22, 0.05);
}

.cloud-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.cloud-row {
  display: flex;
  align-items: center;
  gap: 1.25rem;
  padding: 1.25rem;
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 8px;
}

.provider-icon { width: 32px; height: 32px; background-size: contain; background-repeat: no-repeat; }
.provider-icon.azure { background-image: url('https://upload.wikimedia.org/wikipedia/commons/f/fa/Microsoft_Azure.svg'); }
.provider-icon.aws { background-image: url('https://upload.wikimedia.org/wikipedia/commons/9/93/Amazon_Web_Services_Logo.svg'); }

.cloud-info { flex: 1; display: flex; flex-direction: column; }
.provider-name { font-weight: 700; font-size: 1rem; }
.subscription-id { font-family: monospace; font-size: 0.75rem; color: #64748b; }

.status-indicator { font-size: 0.7rem; font-weight: 800; letter-spacing: 0.5px; text-transform: uppercase; }
.status-indicator.online { color: #10b981; }

/* Identity Section */
.identity-info {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.info-row {
  display: flex;
  justify-content: space-between;
  padding: 0.75rem 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.info-label {
  color: #64748b;
  font-size: 0.85rem;
  font-weight: 600;
}

.info-value {
  font-weight: 700;
  font-size: 0.95rem;
}

.badge.channel {
  background: rgba(16, 185, 129, 0.1);
  color: #10b981;
  border: 1px solid rgba(16, 185, 129, 0.2);
}

.identity-edit {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.standard-input {
  width: 100%;
  background: #0f172a;
  border: 1px solid var(--border-color);
  padding: 0.85rem;
  border-radius: 8px;
  color: white;
  font-family: monospace;
  font-size: 1rem;
}

.standard-input:focus {
  outline: none;
  border-color: var(--dragon-red);
}

.edit-actions {
  display: flex;
  gap: 1rem;
  margin-top: 1rem;
}

.btn-sm {
  padding: 0.5rem 1rem;
  font-size: 0.8rem;
  align-self: flex-start;
}

.otp-box {
  background: rgba(0, 0, 0, 0.3);
  padding: 1.5rem;
  border-radius: 12px;
  border: 1px dashed var(--dragon-red);
  text-align: center;
}

.verification-hint {
  font-size: 0.85rem;
  color: #94a3b8;
  margin-bottom: 1rem;
}

.otp-input {
  background: transparent;
  border: none;
  border-bottom: 2px solid var(--dragon-red);
  font-size: 2rem;
  font-weight: 800;
  color: white;
  text-align: center;
  letter-spacing: 0.5rem;
  width: 100%;
  max-width: 200px;
}

.otp-input:focus { outline: none; }

.alert-banner {
  position: fixed;
  bottom: 2.5rem;
  right: 2.5rem;
  padding: 1.25rem 2rem;
  border-radius: 8px;
  background: #0f172a;
  border: 1px solid var(--border-color);
  display: flex;
  align-items: center;
  gap: 1rem;
  font-weight: 600;
  box-shadow: 0 20px 40px rgba(0,0,0,0.5);
  z-index: 1000;
}

.alert-banner.success { border-bottom: 4px solid #10b981; }
.alert-banner.error { border-bottom: 4px solid var(--dragon-red); }

@keyframes pageReveal {
  from { opacity: 0; transform: translateY(15px); }
  to { opacity: 1; transform: translateY(0); }
}

.slide-up-enter-active, .slide-up-leave-active { transition: all 0.4s; }
.slide-up-enter-from { opacity: 0; transform: translateY(20px); }
.slide-up-leave-to { opacity: 0; transform: scale(0.95); }

@media (max-width: 900px) {
  .settings-grid { grid-template-columns: 1fr; }
}
</style>
