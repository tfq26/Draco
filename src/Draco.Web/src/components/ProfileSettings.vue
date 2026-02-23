<template>
  <div class="profile-container">
    <div class="profile-card glass">
      <div class="profile-header">
        <div class="avatar-ring">
          <div class="avatar">
            {{ userInitial }}
          </div>
        </div>
        <div class="user-info">
          <h1>{{ name || 'Cloud Navigator' }}</h1>
          <p class="phone-badge">{{ formatPhone(phone) }}</p>
        </div>
      </div>

      <div class="settings-grid">
        <!-- Schedule Section -->
        <section class="settings-section">
          <h3>Pulse Report Schedule 📅</h3>
          <p class="section-desc">Define how often you want to receive AI-driven cloud health summaries.</p>
          
          <div class="control-group">
            <label>Frequency</label>
            <div class="toggle-group">
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

          <div class="options-group">
            <div class="option-item">
              <div class="option-info">
                <strong>Cost Analysis</strong>
                <span>Detect leaks and optimization opportunities</span>
              </div>
              <input type="checkbox" v-model="schedule.includeCostAnalysis" />
            </div>
            <div class="option-item">
              <div class="option-info">
                <strong>Security Health</strong>
                <span>Identify immediate risks and long-term planning</span>
              </div>
              <input type="checkbox" v-model="schedule.includeSecurityHealth" />
            </div>
            <div class="option-item">
              <div class="option-info">
                <strong>Active Status</strong>
                <span>Enable or disable the reporting loop</span>
              </div>
              <input type="checkbox" v-model="schedule.isActive" />
            </div>
          </div>

          <button class="save-btn" @click="saveSchedule" :disabled="isLoading">
            <span v-if="isLoading">Synchronizing...</span>
            <span v-else>Update Pulse Schedule</span>
          </button>
        </section>

        <!-- Connected Clouds -->
        <section class="settings-section">
          <h3>Connected Infrastructure ☁️</h3>
          <div class="cloud-list">
            <div v-for="cloud in connections" :key="cloud.subscriptionId" class="cloud-item glass-dark">
              <div class="cloud-icon" :class="cloud.provider.toLowerCase()"></div>
              <div class="cloud-details">
                <strong>{{ cloud.provider }}</strong>
                <span>{{ cloud.subscriptionId }}</span>
              </div>
              <div class="status-dot active"></div>
            </div>
            <div v-if="connections.length === 0" class="empty-clouds">
              No cloud accounts connected yet.
            </div>
          </div>
          <button class="add-btn" @click="goToSetup">+ Connect New Cloud</button>
        </section>
      </div>

      <div v-if="notification" class="notification" :class="notification.type">
        {{ notification.message }}
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, computed } from 'vue';

const phone = ref(localStorage.getItem('draco_user_phone') || '');
const name = ref(localStorage.getItem('draco_user_name') || '');
const connections = ref([]);
const isLoading = ref(false);
const notification = ref(null);

const frequencies = ['Daily', 'Weekly', 'Monthly'];

const schedule = ref({
  frequency: 'Weekly',
  includeCostAnalysis: true,
  includeSecurityHealth: true,
  isActive: true
});

const userInitial = computed(() => name.value ? name.value.charAt(0).toUpperCase() : 'D');

const formatPhone = (p) => {
  if (!p) return '';
  return p.replace(/(\+\d{1})(\d{3})(\d{3})(\d{4})/, '$1 ($2) $3-$4');
};

const fetchProfile = async () => {
  if (!phone.value) return;
  
  try {
    const res = await fetch(`http://localhost:5020/api/auth/check-user?phone=${encodeURIComponent(phone.value)}`);
    const data = await res.json();
    if (data.exists) {
        // In a real app we'd fetch full account details here
        // For demo we assume the user is the one in localStorage
    }

    const schedRes = await fetch(`http://localhost:5020/api/reports/schedule?phone=${encodeURIComponent(phone.value)}`);
    if (schedRes.ok) {
        const schedData = await schedRes.json();
        schedule.value = {
            frequency: schedData.frequency,
            includeCostAnalysis: schedData.includeCostAnalysis,
            includeSecurityHealth: schedData.includeSecurityHealth,
            isActive: schedData.isActive
        };
    }
  } catch (err) {
    console.error("Failed to fetch profile", err);
  }
};

const saveSchedule = async () => {
  isLoading.value = true;
  try {
    const res = await fetch('http://localhost:5020/api/reports/schedule', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        phone: phone.value,
        ...schedule.value
      })
    });

    if (res.ok) {
      showNotification('Schedule synchronized successfully! 🚀', 'success');
    } else {
      throw new Error('Sync failed');
    }
  } catch (err) {
    showNotification('Failed to update schedule. ⚠️', 'error');
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

onMounted(() => {
  fetchProfile();
  // Mock connections if none found
  connections.value = [
    { provider: 'Azure', subscriptionId: 'OAuth-Managed (Prod-1)' }
  ];
});
</script>

<style scoped>
.profile-container {
  min-height: 100vh;
  padding: 4rem 2rem;
  background: radial-gradient(circle at top right, #1a1a2e, #16213e);
  color: white;
  display: flex;
  justify-content: center;
}

.profile-card {
  width: 100%;
  max-width: 900px;
  padding: 3rem;
  border-radius: 2rem;
  animation: fadeIn 0.8s ease-out;
}

.glass {
  background: rgba(255, 255, 255, 0.03);
  backdrop-filter: blur(20px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
}

.profile-header {
  display: flex;
  align-items: center;
  gap: 2rem;
  margin-bottom: 4rem;
}

.avatar-ring {
  padding: 4px;
  background: linear-gradient(135deg, #4facfe 0%, #00f2fe 100%);
  border-radius: 50%;
}

.avatar {
  width: 80px;
  height: 80px;
  background: #1a1a2e;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2.5rem;
  font-weight: bold;
}

.user-info h1 {
  font-size: 2.5rem;
  margin: 0;
  background: linear-gradient(to right, #fff, #ccc);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
}

.phone-badge {
  color: #4facfe;
  font-family: monospace;
  font-size: 1.1rem;
  margin-top: 0.5rem;
}

.settings-grid {
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 3rem;
}

.settings-section {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.settings-section h3 {
  font-size: 1.5rem;
  margin: 0;
}

.section-desc {
  color: #888;
  font-size: 0.95rem;
  margin: 0;
}

.control-group label {
  display: block;
  margin-bottom: 0.75rem;
  color: #aaa;
  font-size: 0.9rem;
}

.toggle-group {
  display: flex;
  background: rgba(255, 255, 255, 0.05);
  padding: 0.4rem;
  border-radius: 0.8rem;
  gap: 0.4rem;
}

.toggle-group button {
  flex: 1;
  padding: 0.6rem;
  border: none;
  background: transparent;
  color: #888;
  border-radius: 0.6rem;
  cursor: pointer;
  transition: all 0.3s;
}

.toggle-group button.active {
  background: #4facfe;
  color: white;
  box-shadow: 0 4px 12px rgba(79, 172, 254, 0.3);
}

.options-group {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.option-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.2rem;
  background: rgba(255, 255, 255, 0.02);
  border-radius: 1rem;
  border: 1px solid rgba(255, 255, 255, 0.05);
}

.option-info {
  display: flex;
  flex-direction: column;
}

.option-info span {
  font-size: 0.85rem;
  color: #666;
}

.save-btn {
  margin-top: 1rem;
  padding: 1.2rem;
  border-radius: 1rem;
  border: none;
  background: linear-gradient(to right, #4facfe 0%, #00f2fe 100%);
  color: white;
  font-weight: 600;
  cursor: pointer;
  transition: transform 0.2s, opacity 0.2s;
}

.save-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  filter: brightness(1.1);
}

.cloud-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.cloud-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
  border-radius: 1rem;
}

.glass-dark {
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.05);
}

.cloud-icon {
  width: 32px;
  height: 32px;
  background-size: contain;
  background-repeat: no-repeat;
}

.cloud-icon.azure { background-image: url('https://upload.wikimedia.org/wikipedia/commons/f/fa/Microsoft_Azure.svg'); }
.cloud-icon.aws { background-image: url('https://upload.wikimedia.org/wikipedia/commons/9/93/Amazon_Web_Services_Logo.svg'); }

.cloud-details {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.cloud-details span {
  font-size: 0.8rem;
  color: #666;
  font-family: monospace;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #4facfe;
  box-shadow: 0 0 10px #4facfe;
}

.add-btn {
  background: transparent;
  border: 1px dashed #4facfe;
  color: #4facfe;
  padding: 1rem;
  border-radius: 1rem;
  cursor: pointer;
  margin-top: 1rem;
}

.notification {
  position: absolute;
  bottom: 2rem;
  left: 50%;
  transform: translateX(-50%);
  padding: 1rem 2rem;
  border-radius: 2rem;
  animation: slideUp 0.4s ease-out;
}

.notification.success { background: #00c853; color: white; }
.notification.error { background: #ff5252; color: white; }

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}

@keyframes slideUp {
  from { transform: translate(-50%, 100%); opacity: 0; }
  to { transform: translate(-50%, 0); opacity: 1; }
}

@media (max-width: 768px) {
  .settings-grid {
    grid-template-columns: 1fr;
  }
}
</style>
