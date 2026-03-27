<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { authClient } from '../lib/auth';

const props = defineProps<{
  initialMode?: 'login' | 'signup'
}>();

const mode = ref(props.initialMode || 'login');
const email = ref('');
const name = ref('');
const password = ref('');
const isLoading = ref(false);
const error = ref('');

const toggleMode = () => {
  mode.value = mode.value === 'login' ? 'signup' : 'login';
  error.value = '';
};

const handleAuth = async () => {
  error.value = '';
  isLoading.value = true;
  
  try {
    const result = mode.value === 'login'
      ? await authClient.signIn.email({
          email: email.value,
          password: password.value,
        })
      : await authClient.signUp.email({
          email: email.value,
          password: password.value,
          name: name.value,
        });

    if (result.error) {
      throw result.error;
    }
    
    // Successful login, redirect to profile
    window.location.assign('/profile');
  } catch (err: any) {
    console.error("[UnifiedAuth] Authentication Error:", err);
    error.value = err.message || "Authentication failed. Please verify your credentials.";
  } finally {
    isLoading.value = false;
  }
};

const handleSocialSignIn = async (provider: 'google' | 'github') => {
  error.value = '';
  try {
    const result = await authClient.signIn.social({
      provider,
      callbackURL: window.location.origin + '/profile'
    });
    if (result.error) throw result.error;
  } catch (err: any) {
    console.error("[UnifiedAuth] Social Sign-in Error:", err);
    error.value = err.message || `Failed to sign in with ${provider}`;
  }
};
</script>

<template>
  <div class="auth-box glass">
    <div class="auth-header">
      <div class="auth-nav">
        <button 
          :class="{ active: mode === 'login' }" 
          @click="mode = 'login'"
        >Sign In</button>
        <button 
          :class="{ active: mode === 'signup' }" 
          @click="mode = 'signup'"
        >Create Account</button>
      </div>
      <h2 class="title">{{ mode === 'login' ? 'Welcome Back' : 'Join Draco' }}</h2>
      <p class="subtitle">
        {{ mode === 'login' ? 'Access your cloud management dashboard.' : 'Start monitoring your cloud resources for free.' }}
      </p>
    </div>

    <div class="auth-body">
      <div class="auth-flow-container">
        <Transition name="fade-slide" mode="out-in">
          <div :key="mode" class="form-container">
            <div v-if="mode === 'signup'" class="input-field">
              <label>Full Name</label>
              <input 
                v-model="name" 
                type="text" 
                placeholder="Gingka Hagane"
                @keyup.enter="handleAuth"
              />
            </div>

            <div class="input-field">
              <label>Email Address</label>
              <input 
                v-model="email" 
                type="email" 
                placeholder="name@company.com"
                @keyup.enter="handleAuth"
              />
            </div>

            <div class="input-field">
              <label>Password</label>
              <input 
                v-model="password" 
                type="password" 
                placeholder="••••••••"
                @keyup.enter="handleAuth"
              />
            </div>

            <button class="submit-btn" :disabled="isLoading" @click="handleAuth">
              <span v-if="isLoading" class="loader"></span>
              <span v-else>{{ mode === 'login' ? 'Continue' : 'Init Account' }}</span>
            </button>
          </div>
        </Transition>

        <div class="social-divider">
          <span>OR CONTINUE WITH</span>
        </div>

        <div class="social-actions">
          <button class="social-btn github" @click="handleSocialSignIn('github')">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
            Continue with GitHub
          </button>
          <button class="social-btn google" @click="handleSocialSignIn('google')">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M7 11v2.4h3.97c-.16 1.029-1.2 3.02-3.97 3.02-2.39 0-4.34-1.979-4.34-4.42 0-2.44 1.95-4.42 4.34-4.42 1.36 0 2.27.58 2.79 1.08l1.9-1.83c-1.22-1.14-2.8-1.83-4.69-1.83-3.87 0-7 3.13-7 7s3.13 7 7 7c4.04 0 6.721-2.84 6.721-6.84 0-.46-.051-.81-.111-1.16h-6.61zm0 0z"/></svg>
            Continue with Google
          </button>
        </div>
      </div>

      <div v-if="error" class="error-toast">{{ error }}</div>
    </div>
  </div>
</template>

<style scoped>
.auth-box {
  width: 100%;
  max-width: 440px;
  padding: 3rem;
  border-radius: var(--radius-lg);
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.auth-body {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.auth-flow-container {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.label-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.auth-nav {
  display: flex;
  gap: 2rem;
  margin-bottom: 2rem;
  border-bottom: 1px solid var(--border-color);
}

.auth-nav button {
  background: none;
  border: none;
  color: var(--sub-text-color);
  font-weight: 700;
  font-size: 0.9rem;
  padding-bottom: 1rem;
  cursor: pointer;
  position: relative;
  transition: all 0.2s;
}

.auth-nav button.active {
  color: var(--text-color);
}

.auth-nav button.active::after {
  content: '';
  position: absolute;
  bottom: -1px;
  left: 0;
  right: 0;
  height: 2px;
  background: var(--dragon-red);
}

.title {
  font-size: 2rem;
  font-weight: 900;
  letter-spacing: -1px;
  margin: 0;
}

.subtitle {
  color: var(--sub-text-color);
  font-size: 0.95rem;
  margin-top: 0.5rem;
}

.form-container {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.input-field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.input-field label {
  font-size: 0.75rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: var(--meteo-bronze);
}

.label-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.link-btn {
  background: none;
  border: none;
  color: var(--dragon-red);
  font-size: 0.7rem;
  font-weight: 700;
  cursor: pointer;
  padding: 0;
}

.magic-link-hint {
  font-size: 0.85rem;
  color: var(--sub-text-color);
  background: rgba(255, 255, 255, 0.02);
  padding: 1rem;
  border-radius: var(--radius-sm);
  border: 1px dashed var(--border-color);
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.success-state {
  text-align: center;
  padding: 1rem 0;
  animation: fadeIn 0.4s ease;
}

.sent-icon {
  font-size: 3rem;
  margin-bottom: 1.5rem;
}

.secondary-btn.ghost {
  background: transparent;
  border: 1px solid var(--border-color);
  color: var(--text-color);
  padding: 0.8rem 1.5rem;
  border-radius: var(--radius-sm);
  font-weight: 700;
  cursor: pointer;
  width: 100%;
}

.mt-4 { margin-top: 1rem; }

.input-field input {
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 0.9rem 1.1rem;
  color: white;
  font-size: 1rem;
  transition: all 0.2s;
}

.input-field input:focus {
  outline: none;
  border-color: var(--dragon-red);
  background: rgba(255, 255, 255, 0.02);
}

.submit-btn {
  background: var(--dragon-red);
  color: white;
  border: 1px solid #7c0d11;
  padding: 1.1rem;
  border-radius: var(--radius-sm);
  font-weight: 800;
  font-size: 1rem;
  cursor: pointer;
  transition: all 0.3s;
  margin-top: 0.5rem;
}

.submit-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 10px 25px rgba(177, 17, 22, 0.3);
}

.social-divider {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin: 0.5rem 0;
}

.social-divider::before, .social-divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border-color);
}

.social-divider span {
  font-size: 0.65rem;
  font-weight: 800;
  color: var(--sub-text-color);
  letter-spacing: 1px;
}

.social-actions {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.social-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 0.75rem;
  color: white;
  font-weight: 600;
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.2s;
}

.social-btn:hover {
  background: rgba(255, 255, 255, 0.05);
  border-color: #64748b;
}

.error-toast {
  background: rgba(177, 17, 22, 0.1);
  border: 1px solid rgba(177, 17, 22, 0.2);
  color: #ff5252;
  padding: 1rem;
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
  font-weight: 600;
  text-align: center;
  margin-top: 1rem;
}

/* Animations */
.fade-slide-enter-active, .fade-slide-leave-active { transition: all 0.3s ease; }
.fade-slide-enter-from { opacity: 0; transform: translateX(10px); }
.fade-slide-leave-to { opacity: 0; transform: translateX(-10px); }

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(10px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
