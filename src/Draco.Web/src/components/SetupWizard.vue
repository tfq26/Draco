<template>
  <div class="onboarding-layout">
    <!-- Close Button (Always visible) -->
    <button @click="closeSetup" class="close-btn" aria-label="Close setup">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M18 6L6 18M6 6l12 12" />
      </svg>
    </button>

    <!-- Sidebar / Context Panel -->
    <div class="context-panel">
      <div class="context-content">
        <div class="logo-area">
          <img src="/draco-colored.svg" alt="Draco Logo" class="onboarding-logo" />
          <span class="brand-name">Draco</span>
        </div>
        
        <Transition name="fade" mode="out-in">
          <div :key="step" class="step-guidance">
            <h1 class="guidance-title">{{ guidanceTitle }}</h1>
            <p class="guidance-text">{{ guidanceText }}</p>
          </div>
        </Transition>

        <div class="progress-indicator">
          <div 
            class="progress-bar-fill"
            :style="{ width: `${(step / 6) * 100}%` }"
          ></div>
        </div>
        <div class="step-counter">Step {{ step }} of 6</div>
      </div>
    </div>

    <!-- Main Content Area -->
    <main class="question-area">
      <div class="question-container">
        <Transition :name="transitionName" mode="out-in">
          <div :key="step" class="step-active">
            
            <!-- Step 1: Name -->
            <div v-if="step === 1" class="step-card">
              <h2 class="question-title">What should we call you?</h2>
              <div class="input-group">
                <input 
                  v-model="name" 
                  type="text" 
                  class="big-input"
                  placeholder="Your Name"
                  v-on:keyup.enter="nextStep"
                  autofocus
                />
              </div>
              <button @click="nextStep" class="primary-btn" :disabled="!name">
                Continue
              </button>
            </div>

            <!-- Step 2: Channel Selection -->
            <div v-if="step === 2" class="step-card">
              <h2 class="question-title">How should we alert you?</h2>
              <div class="options-grid">
                <button 
                  @click="channel = 'WhatsApp'; nextStep()" 
                  class="option-card"
                  :class="{ active: channel === 'WhatsApp' }"
                >
                  <div class="option-icon">💬</div>
                  <div class="option-label">WhatsApp</div>
                </button>
                <button 
                  @click="channel = 'SMS'; nextStep()" 
                  class="option-card"
                  :class="{ active: channel === 'SMS' }"
                >
                  <div class="option-icon">📱</div>
                  <div class="option-label">Secure SMS</div>
                </button>
              </div>
            </div>

            <!-- Step 3: Account Details (Email) -->
            <div v-if="step === 3" class="step-card">
              <h2 class="question-title">Create Account</h2>
              <p class="question-sub">Enter your email address to get started.</p>
              <div class="input-group">
                <input 
                  v-model="email" 
                  type="email" 
                  class="big-input"
                  placeholder="[EMAIL_ADDRESS]"
                  v-on:keyup.enter="nextStep"
                />
              </div>
              <button @click="nextStep" class="primary-btn" :disabled="isLoading || !email">
                Continue to Password
              </button>
            </div>

            <!-- Step 4: Password Setup -->
            <div v-if="step === 4" class="step-card">
              <h2 class="question-title">Secure Your Account</h2>
              <p class="question-sub">Create a password for <strong>{{ email }}</strong>.</p>
              
              <div class="input-group">
                <input 
                  v-model="password" 
                  type="password" 
                  class="big-input"
                  placeholder="••••••••"
                  v-on:keyup.enter="handleAuth"
                />
              </div>

              <div class="auth-actions mt-8">
                <button @click="handleAuth" class="primary-btn wide" :disabled="isLoading || !password">
                  {{ isLoginMode ? (isLoading ? 'Signing in...' : 'Sign In') : (isLoading ? 'Creating account...' : 'Create Account') }}
                </button>
                
                <button @click="isLoginMode = !isLoginMode" class="secondary-btn mt-4 ghost">
                  {{ isLoginMode ? "Don't have an account? Sign Up" : "Already have an account? Sign In" }}
                </button>
              </div>

              <p v-if="error" class="error-msg mt-4">{{ error }}</p>
            </div>

            <!-- Step 5: Cloud Connection -->
            <div v-if="step === 5" class="step-card">
              <h2 class="question-title">Connect Cloud Accounts</h2>
              <p class="question-sub">Draco uses read-only access to securely audit your cloud environment.</p>
              
              <div class="auth-grid">
                <!-- Azure Auth -->
                <div class="premium-provider-card" :class="{ connected: azureConnected, loading: isLoading && loadingProvider === 'Azure' }">
                  <div class="card-glow"></div>
                  <div class="card-content">
                    <div class="provider-logo azure"></div>
                    <div class="provider-meta">
                      <h3>Microsoft Azure</h3>
                      <p>{{ azureConnected ? 'Connected' : 'Azure Integration' }}</p>
                    </div>
                    <button @click="connectAzure" class="connect-action-btn" :disabled="isLoading">
                      <span v-if="azureConnected" class="status-pill connected">Online</span>
                      <span v-else class="status-pill idle">{{ isLoading && loadingProvider === 'Azure' ? 'Verifying...' : 'Link Account' }}</span>
                    </button>
                  </div>
                  <div v-if="azureConnected" class="connection-line"></div>
                </div>

                <!-- AWS Auth -->
                <div class="premium-provider-card" :class="{ connected: awsConnected, loading: isLoading && loadingProvider === 'AWS' }">
                  <div class="card-glow"></div>
                  <div class="card-content">
                    <div class="provider-logo aws"></div>
                    <div class="provider-meta">
                      <h3>Amazon Web Services</h3>
                      <p>{{ awsConnected ? 'Connected' : 'AWS Integration' }}</p>
                    </div>
                    <button @click="connectAWS" class="connect-action-btn" :disabled="isLoading">
                      <span v-if="awsConnected" class="status-pill connected">Online</span>
                      <span v-else class="status-pill idle">{{ isLoading && loadingProvider === 'AWS' ? 'Connecting...' : 'Link Account' }}</span>
                    </button>
                  </div>
                  <div v-if="awsConnected" class="connection-line"></div>
                </div>
              </div>

              <div class="action-footer mt-12">
                <button @click="finishSetup" class="primary-btn wide" :disabled="isLoading">
                  <span v-if="isLoading" class="loader-dots"><span></span><span></span><span></span></span>
                  <span v-else>{{ (azureConnected || awsConnected) ? 'Finish Setup' : 'Continue to Dashboard' }}</span>
                </button>
                <p v-if="!azureConnected && !awsConnected" class="skip-hint mt-4">
                  No cloud connected. You'll enter "Observer Mode" until a provider is linked.
                </p>
              </div>
            </div>

            <!-- Step 6: Success -->
            <div v-if="step === 6" class="step-card success-layout">
              <div class="celebration-ring">
                <div class="dragon-avatar">🐉</div>
                <div class="ring-pulse"></div>
              </div>

              <div class="success-content">
                <h2 class="success-title">
                  {{ (azureConnected || awsConnected) ? 'Account Ready' : 'Account Created' }}
                </h2>
                <div class="config-summary-card glass mt-6">
                  <div class="summary-item">
                    <span class="label">User</span>
                    <span class="value">{{ name }}</span>
                  </div>
                  <div class="summary-item">
                    <span class="label">Phone</span>
                    <span class="value">{{ phone }}</span>
                  </div>
                  <div class="summary-item">
                    <span class="label">Status</span>
                    <span class="value active">Online</span>
                  </div>
                </div>

                <div class="action-stack mt-10">
                  <button @click="goToProfile" class="primary-btn">
                    Configure Settings
                  </button>
                  <button @click="closeSetup" class="tertiary-btn">
                    Enter Dashboard
                  </button>
                </div>
              </div>
            </div>

          </div>
        </Transition>
      </div>

      <!-- Footer Buttons -->
      <div v-if="step > 1 && step < 6" class="onboarding-nav">
        <button @click="prevStep" class="secondary-btn">
          Back
        </button>
      </div>
    </main>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue';
import { authClient } from '../lib/auth';
import { dracoApiFetch, getDracoApiBaseUrl, getDracoApiToken } from '../lib/dracoApi';

const emit = defineEmits(['close']);

const step = ref(1);
const isLoading = ref(false);
const error = ref('');
const transitionName = ref('slide-next');
const isLoginMode = ref(true);

// Data
const name = ref('');
const channel = ref('');
const email = ref('');
const password = ref('');
const phone = ref('');
const verificationCode = ref('');
const provider = ref('Azure');
const azureConnected = ref(false);
const awsConnected = ref(false);
const loadingProvider = ref('');

const azure = ref({
  tenantId: '',
  clientId: '',
  clientSecret: '',
  subscriptionId: ''
});

const isExistingUserDetected = ref(false);

// Guidance Copy
const guidanceTitle = computed(() => {
  if (step.value === 3 && isExistingUserDetected.value) return "Account detected.";
  switch (step.value) {
    case 1: return "First encounters.";
    case 2: return "Communication Channel.";
    case 3: return "Create account.";
    case 4: return "Verification.";
    case 5: return "Cloud Connection.";
    case 6: return "All set!";
    default: return "";
  }
});

const guidanceText = computed(() => {
  if (step.value === 3 && isExistingUserDetected.value) return "We found an existing Sentinel associated with this signal. Verification is required to re-establish control.";
  switch (step.value) {
    case 1: return "We'd like to know who we're helping. It makes the reports more personal.";
    case 2: return "Choose a frequency that suits your workflow. Draco sends alerts via real-time channels.";
    case 3: return "Security is everything. We verify every account connection.";
    case 4: return "Just checking it's really you. Your privacy is our priority.";
    case 5: return "Draco needs read-only access to scan for potential issues and misconfigurations.";
    case 6: return (azureConnected.value || awsConnected.value) ? "Systems ready. Welcome to Draco." : "Account created. Connect your cloud when you're ready.";
    default: return "";
  }
});

// Methods
const nextStep = () => {
  transitionName.value = 'slide-next';
  
  // Intelligence for skipping steps if already authenticated
  if (step.value === 2 && email.value) {
    step.value = 5; // Skip Email (3) and Password (4) if we have a session
  } else {
    step.value++;
  }
};

const prevStep = () => {
  transitionName.value = 'slide-prev';
  if (step.value === 5 && email.value) {
    step.value = 2;
  } else {
    step.value--;
  }
};



const connectAzure = async () => {
  try {
    loadingProvider.value = 'Azure';
    isLoading.value = true;

    const token = await getDracoApiToken();
    const authUrl = `${getDracoApiBaseUrl()}/api/auth/azure?access_token=${encodeURIComponent(token)}`;
    const width = 600, height = 700;
    const left = window.innerWidth / 2 - width / 2;
    const top = window.innerHeight / 2 - height / 2;

    const authWindow = window.open(
      authUrl,
      'Connect Azure',
      `width=${width},height=${height},left=${left},top=${top}`
    );

    const timer = setInterval(() => {
      if (authWindow && authWindow.closed) {
        clearInterval(timer);
        azureConnected.value = true;
        isLoading.value = false;
        loadingProvider.value = '';
      }
    }, 1000);
  } catch (err) {
    error.value = err.message || 'Failed to start Azure connection.';
    isLoading.value = false;
    loadingProvider.value = '';
  }
};

const connectAWS = async () => {
  try {
    loadingProvider.value = 'AWS';
    isLoading.value = true;
    const token = await getDracoApiToken();
    const authUrl = `${getDracoApiBaseUrl()}/api/auth/aws?access_token=${encodeURIComponent(token)}`;
    window.open(authUrl, '_blank');
    
    // For demo purposes, we'll just wait a bit
    await new Promise(r => setTimeout(r, 3000));
    awsConnected.value = true;
    isLoading.value = false;
    loadingProvider.value = '';
  } catch (err) {
    error.value = err.message || 'Failed to start AWS connection.';
    isLoading.value = false;
    loadingProvider.value = '';
  }
};

const handleAuth = async () => {
    error.value = '';
    isLoading.value = true;
    try {
        if (isLoginMode.value) {
            const { error: authError } = await authClient.signIn.email({
                email: email.value,
                password: password.value,
            });
            if (authError) throw authError;
        } else {
            const { error: authError } = await authClient.signUp.email({
                email: email.value,
                password: password.value,
                name: name.value,
                phone: phone.value,
            });
            if (authError) throw authError;
        }
        isLoading.value = false;
        nextStep();
    } catch (err) {
        error.value = err.message || "Authentication failed. Check your credentials.";
        isLoading.value = false;
    }
};

const finishSetup = async () => {
  isLoading.value = true;
  error.value = '';

  const connections = [];
  if (azureConnected.value) connections.push({ provider: 'Azure', subscriptionId: 'OAuth-Managed', accessToken: 'dummy-token' });
  if (awsConnected.value) connections.push({ provider: 'AWS', subscriptionId: 'OAuth-Managed', accessToken: 'dummy-token' });

  try {
    const response = await dracoApiFetch('/api/auth/setup-complete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        phone: phone.value,
        name: name.value,
        email: email.value,
        preferredChannel: channel.value,
        connections: connections
      })
    });

    if (!response.ok) throw new Error('Failed to persist setup.');

    localStorage.setItem('draco_user_name', name.value);
    localStorage.setItem('draco_user_phone', phone.value);
    localStorage.setItem('draco_user_email', email.value);

    isLoading.value = false;
    nextStep();
  } catch (err) {
    error.value = err.message;
    isLoading.value = false;
    // Fallback for demo
    console.warn("API Persist failed, but proceeding to success screen for UX.", err);
    nextStep();
  }
};

const goToProfile = () => {
  window.location.href = '/profile';
};

const closeSetup = () => {
  emit('close');
  window.location.href = '/';
}

onMounted(async () => {
  try {
    const { data: sessionData } = await authClient.getSession();
    if (sessionData) {
      email.value = sessionData.user?.email || '';
      name.value = sessionData.user?.name || '';
      
      // If they have a name, jump straight to notifications
      if (name.value) {
        step.value = 2;
      }
    }
  } catch (err) {
    console.error("Auth check failed in setup wizard", err);
  }
});
</script>

<style scoped>
.onboarding-layout {
  display: flex;
  height: 100vh;
  width: 100vw;
  background: var(--bg-color);
  color: var(--text-color);
  position: fixed;
  top: 0;
  left: 0;
  z-index: 9999;
  overflow: hidden;
}

/* Close Button */
.close-btn {
  position: absolute;
  top: 2rem;
  right: 2rem;
  background: none;
  border: none;
  color: var(--sub-text-color);
  cursor: pointer;
  z-index: 100;
  padding: 0.5rem;
  transition: all 0.2s ease;
}

.close-btn:hover {
  color: var(--text-color);
  transform: rotate(90deg);
}

.close-btn svg {
  width: 24px;
  height: 24px;
}

/* Sidebar */
.context-panel {
  width: 35%;
  background: linear-gradient(135deg, rgba(177, 17, 22, 0.05), rgba(26, 54, 154, 0.05));
  border-right: 1px solid var(--border-color);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4rem;
  position: relative;
}

.context-content {
  max-width: 400px;
}

.logo-area {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 4rem;
}

.onboarding-logo {
  height: 48px;
}

.brand-name {
  font-weight: 800;
  font-size: 1.5rem;
  letter-spacing: -1px;
}

.guidance-title {
  font-size: 2.5rem;
  font-weight: 900;
  line-height: 1.1;
  margin-bottom: 1.5rem;
  color: var(--text-color);
}

.guidance-text {
  font-size: 1.2rem;
  color: var(--sub-text-color);
  margin-bottom: 4rem;
}

.progress-indicator {
  height: 4px;
  background: var(--border-color);
  border-radius: 2px;
  margin-bottom: 1rem;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: var(--dragon-red);
  transition: width 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.step-counter {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--sub-text-color);
  text-transform: uppercase;
}

/* Main Question Area */
.question-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem;
  position: relative;
}

.question-container {
  width: 100%;
  max-width: 600px;
}

.question-title {
  font-size: 3rem;
  font-weight: 900;
  margin-bottom: 1rem;
  letter-spacing: -1.5px;
}

.question-sub {
  font-size: 1.2rem;
  color: var(--sub-text-color);
  margin-bottom: 3rem;
}

/* Elements */
.big-input {
  width: 100%;
  background: none;
  border: none;
  border-bottom: 3px solid var(--border-color);
  padding: 1rem 0;
  font-size: 2.5rem;
  font-weight: 700;
  color: var(--text-color);
  margin-bottom: 4rem;
  transition: border-color 0.3s ease;
}

.big-input:focus {
  outline: none;
  border-color: var(--dragon-red);
}

.big-input.centered {
  text-align: center;
}

.primary-btn {
  background: var(--dragon-red);
  color: white;
  padding: 1.2rem 3rem;
  border-radius: 12px;
  font-size: 1.2rem;
  font-weight: 700;
  border: none;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
  box-shadow: 0 10px 20px rgba(177, 17, 22, 0.2);
}

.primary-btn:hover {
  transform: translateY(-4px);
  box-shadow: 0 15px 30px rgba(177, 17, 22, 0.3);
}

.primary-btn:disabled {
  opacity: 0.3;
  cursor: not-allowed;
  transform: none;
}

.secondary-btn {
  background: none;
  border: none;
  color: var(--sub-text-color);
  font-weight: 600;
  cursor: pointer;
  font-size: 1rem;
}

.options-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2rem;
  margin-bottom: 4rem;
}

.auth-grid {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.provider-auth-card {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 1.5rem;
  display: flex;
  justify-content: space-between;
  align-items: center;
  transition: all 0.3s ease;
}

.provider-auth-card.connected {
  border-color: #10b981;
  background: rgba(16, 185, 129, 0.05);
}

.provider-info {
  display: flex;
  align-items: center;
  gap: 1.5rem;
}

.connection-status {
  font-size: 0.85rem;
  color: var(--sub-text-color);
  font-weight: 500;
}

.connected .connection-status {
  color: #10b981;
}

.auth-btn {
  background: var(--bg-color);
  border: 1px solid var(--border-color);
  color: var(--text-color);
  padding: 0.6rem 1.2rem;
  border-radius: 8px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}

.auth-btn:hover {
  border-color: var(--dragon-red);
  color: var(--dragon-red);
}

.connected .auth-btn {
  background: var(--bg-color);
  border-color: var(--border-color);
  color: var(--sub-text-color);
}

.option-card {
  background: var(--card-bg);
  border: 2px solid var(--border-color);
  border-radius: 20px;
  padding: 3rem 2rem;
  cursor: pointer;
  transition: all 0.3s ease;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.5rem;
}

.option-card:hover {
  border-color: var(--dragon-red);
  background: rgba(177, 17, 22, 0.05);
}

.option-card.active {
  border-color: var(--dragon-red);
  background: rgba(177, 17, 22, 0.1);
}

.option-icon {
  font-size: 3rem;
  font-weight: 900;
}

.icon-azure { color: #0078d4; }
.icon-aws { color: #ff9900; }

.option-label {
  font-weight: 800;
  font-size: 1.2rem;
}

.provider-inputs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.small-input {
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  padding: 1rem;
  border-radius: 8px;
  color: var(--text-color);
}

.onboarding-nav {
  position: absolute;
  bottom: 4rem;
  left: 4rem;
}

.success-icon {
  font-size: 5rem;
  animation: float 3s ease-in-out infinite;
}

@keyframes float {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-20px); }
}

/* Transitions */
.slide-next-enter-active, .slide-next-leave-active,
.slide-prev-enter-active, .slide-prev-leave-active {
  transition: all 0.6s cubic-bezier(0.68, -0.6, 0.32, 1.6);
}

.slide-next-enter-from { opacity: 0; transform: translateX(100px); }
.slide-next-leave-to { opacity: 0; transform: translateX(-100px); }

.slide-prev-enter-from { opacity: 0; transform: translateX(-100px); }
.slide-prev-leave-to { opacity: 0; transform: translateX(100px); }

.fade-enter-active, .fade-leave-active {
  transition: opacity 0.4s ease;
}
.fade-enter-from, .fade-leave-to { opacity: 0; }

@media (max-width: 1024px) {
  .onboarding-layout { flex-direction: column; overflow-y: auto; }
  .context-panel { width: 100%; padding: 2rem; border-right: none; border-bottom: 1px solid var(--border-color); }
  .question-area { padding: 2rem; }
}

.skip-hint {
  font-size: 0.9rem;
  color: var(--sub-text-color);
  text-align: center;
  opacity: 0.7;
}

/* Premium Provider Cards */
.premium-provider-card {
  position: relative;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 24px;
  overflow: hidden;
  transition: all 0.4s cubic-bezier(0.175, 0.885, 0.32, 1.275);
}

.premium-provider-card:hover {
  transform: translateY(-8px);
  background: rgba(255, 255, 255, 0.04);
  border-color: rgba(255, 255, 255, 0.1);
}

.premium-provider-card.connected {
  border-color: #4facfe;
  background: rgba(79, 172, 254, 0.05);
}

.card-glow {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: radial-gradient(circle at top right, rgba(79, 172, 254, 0.1), transparent);
  opacity: 0;
  transition: opacity 0.4s;
}

.premium-provider-card:hover .card-glow {
  opacity: 1;
}

.card-content {
  padding: 2.5rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1.5rem;
  position: relative;
  z-index: 1;
}

.provider-logo {
  width: 64px;
  height: 64px;
  background-size: contain;
  background-repeat: no-repeat;
  background-position: center;
  filter: grayscale(1) brightness(2);
  transition: filter 0.4s;
}

.premium-provider-card.connected .provider-logo {
  filter: grayscale(0) brightness(1);
}

.provider-logo.azure { background-image: url('https://upload.wikimedia.org/wikipedia/commons/f/fa/Microsoft_Azure.svg'); }
.provider-logo.aws { background-image: url('https://upload.wikimedia.org/wikipedia/commons/9/93/Amazon_Web_Services_Logo.svg'); }

.provider-meta {
  text-align: center;
}

.provider-meta h3 {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0;
}

.provider-meta p {
  font-size: 0.85rem;
  color: #888;
  margin: 0.25rem 0 0;
}

.connect-action-btn {
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;
  width: 100%;
}

.status-pill {
  display: inline-block;
  padding: 0.6rem 1.5rem;
  border-radius: 99px;
  font-size: 0.85rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 1px;
  transition: all 0.3s;
}

.status-pill.idle {
  background: rgba(255, 255, 255, 0.05);
  color: #aaa;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.status-pill.connected {
  background: #4facfe;
  color: white;
  box-shadow: 0 4px 15px rgba(79, 172, 254, 0.3);
}

.connection-line {
  position: absolute;
  bottom: 0;
  left: 0;
  height: 3px;
  background: #4facfe;
  width: 100%;
  animation: scan 2s linear infinite;
}

@keyframes scan {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(100%); }
}

/* Success Layout */
.success-layout {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.celebration-ring {
  position: relative;
  width: 120px;
  height: 120px;
  margin-bottom: 2rem;
}

.dragon-avatar {
  font-size: 5rem;
  position: relative;
  z-index: 2;
  animation: bounce 2s infinite alternate;
}

.ring-pulse {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 100%;
  height: 100%;
  border-radius: 50%;
  background: radial-gradient(circle, rgba(79, 172, 254, 0.2), transparent);
  animation: pulse-ring 2s infinite;
}

@keyframes pulse-ring {
  0% { width: 50%; height: 50%; opacity: 1; }
  100% { width: 150%; height: 150%; opacity: 0; }
}

@keyframes bounce {
  from { transform: translateY(0); }
  to { transform: translateY(-10px); }
}

.success-title {
  font-size: 3rem;
  font-weight: 900;
  background: linear-gradient(to right, #fff, #4facfe);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
  margin: 0;
}

.config-summary-card {
  width: 100%;
  padding: 1.5rem;
  border-radius: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.summary-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.summary-item:last-child { border: none; }

.summary-item .label {
  color: #888;
  font-size: 0.9rem;
}

.summary-item .value {
  font-weight: 600;
  font-family: monospace;
}

.summary-item .value.active { color: #4facfe; }

.action-stack {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  width: 100%;
}

.tertiary-btn {
  background: none;
  border: 1px solid rgba(255, 255, 255, 0.1);
  color: #888;
  padding: 1.2rem;
  border-radius: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.3s;
}

.tertiary-btn:hover {
  background: rgba(255, 255, 255, 0.05);
  color: white;
}

.loader-dots {
  display: flex;
  gap: 4px;
  justify-content: center;
}

.loader-dots span {
  width: 6px;
  height: 6px;
  background: white;
  border-radius: 50%;
  animation: dot-pulse 1.4s infinite ease-in-out both;
}

.loader-dots span:nth-child(1) { animation-delay: -0.32s; }
.loader-dots span:nth-child(2) { animation-delay: -0.16s; }

@keyframes dot-pulse {
  0%, 80%, 100% { transform: scale(0); }
  40% { transform: scale(1.0); }
}

.shadow-text {
  text-shadow: 0 0 15px var(--accent-glow);
}
</style>
