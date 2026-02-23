<script setup lang="ts">
import { ref } from 'vue';
import { authClient } from '../lib/auth';

const email = ref('');
const password = ref('');
const isLoading = ref(false);
const error = ref('');

const handleSignIn = async () => {
    error.value = '';
    isLoading.value = true;
    try {
        const { data, error: authError } = await authClient.signIn.email({
            email: email.value,
            password: password.value,
            callbackURL: '/profile'
        });
        if (authError) throw authError;
        
        // Redirect if not handled by callbackURL
        window.location.href = '/profile';
    } catch (err: any) {
        error.value = err.message || "Authentication failed. Check your credentials.";
        isLoading.value = false;
    }
};

const handleSocialSignIn = async (provider: 'google' | 'github') => {
    error.value = '';
    try {
        await authClient.signIn.social({
            provider,
            callbackURL: '/profile'
        });
    } catch (err: any) {
        error.value = `Initialization of ${provider} handshake failed.`;
    }
};
</script>

<template>
  <div class="auth-card-container">
    <div class="auth-card">
      <div class="card-header">
        <h2 class="auth-title">Sign In</h2>
        <p class="auth-sub">Welcome back.</p>
      </div>

      <div class="auth-body">
        <div class="input-group">
          <label for="email">Access Point</label>
          <input 
            id="email"
            v-model="email" 
            type="email" 
            class="premium-input"
            placeholder="[EMAIL_ADDRESS]"
            v-on:keyup.enter="handleSignIn"
            autofocus
          />
        </div>

        <div class="input-group">
          <label for="password">Cryptographic Key</label>
          <input 
            id="password"
            v-model="password" 
            type="password" 
            class="premium-input"
            placeholder="••••••••"
            v-on:keyup.enter="handleSignIn"
          />
        </div>

        <div class="auth-actions">
          <button @click="handleSignIn" class="primary-btn wide" :disabled="isLoading || !email || !password">
            {{ isLoading ? 'Synchronizing...' : 'Login' }}
          </button>
          
          <div class="form-footer">
            <p>Don't have an account? <a href="/register">Signup</a></p>
          </div>

          <div class="social-auth-section">
            <div class="divider">
              <span>OR CONNECT VIA</span>
            </div>
            
            <div class="social-grid">
              <button @click="handleSocialSignIn('github')" class="social-btn github">
                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
                GitHub
              </button>
              <button @click="handleSocialSignIn('google')" class="social-btn google">
                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M7 11v2.4h3.97c-.16 1.029-1.2 3.02-3.97 3.02-2.39 0-4.34-1.979-4.34-4.42 0-2.44 1.95-4.42 4.34-4.42 1.36 0 2.27.58 2.79 1.08l1.9-1.83c-1.22-1.14-2.8-1.83-4.69-1.83-3.87 0-7 3.13-7 7s3.13 7 7 7c4.04 0 6.721-2.84 6.721-6.84 0-.46-.051-.81-.111-1.16h-6.61zm0 0z"/></svg>
                Google
              </button>
            </div>
          </div>
        </div>

        <p v-if="error" class="error-msg">{{ error }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.auth-card-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: calc(100vh - 200px);
  padding: 2rem;
}

.auth-card {
  width: 100%;
  max-width: 480px;
  background: var(--card-bg);
  border: 1px solid var(--border-color);
  border-radius: 24px;
  padding: 3rem;
  backdrop-filter: blur(12px);
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
}

.auth-title {
  font-size: 2.5rem;
  font-weight: 900;
  margin-bottom: 0.5rem;
  letter-spacing: -1px;
}

.auth-sub {
  color: var(--sub-text-color);
  margin-bottom: 2.5rem;
  font-size: 1rem;
}

.input-group {
  margin-bottom: 2rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.input-group label {
  font-size: 0.85rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: var(--meteo-bronze);
}

.premium-input {
  width: 100%;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 1rem;
  font-size: 1.1rem;
  color: var(--text-color);
  transition: all 0.3s ease;
}

.premium-input:focus {
  outline: none;
  border-color: var(--dragon-red);
  background: rgba(255, 255, 255, 0.05);
  box-shadow: 0 0 15px var(--accent-glow);
}

.primary-btn {
  background: var(--dragon-red);
  color: white;
  padding: 1.2rem;
  border-radius: 12px;
  font-size: 1.1rem;
  font-weight: 700;
  border: none;
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
  margin-top: 1rem;
}

.primary-btn:hover:not(:disabled) {
  transform: translateY(-4px);
  box-shadow: 0 10px 20px rgba(177, 17, 22, 0.3);
}

.wide {
  width: 100%;
}

.form-footer {
  margin-top: 2rem;
  text-align: center;
  font-size: 0.9rem;
  color: var(--sub-text-color);
}

.form-footer a {
  color: var(--dragon-red);
  font-weight: 700;
  text-decoration: none;
}

.error-msg {
  color: #ff4d4d;
  background: rgba(255, 77, 77, 0.1);
  padding: 1rem;
  border-radius: 8px;
  margin-top: 1.5rem;
  font-size: 0.9rem;
  text-align: center;
  border: 1px solid rgba(255, 77, 77, 0.2);
}

.social-auth-section {
  margin-top: 2rem;
}

.divider {
  display: flex;
  align-items: center;
  text-align: center;
  margin-bottom: 1.5rem;
}

.divider::before,
.divider::after {
  content: '';
  flex: 1;
  border-bottom: 1px solid var(--border-color);
}

.divider span {
  padding: 0 1rem;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--sub-text-color);
  letter-spacing: 1px;
}

.social-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.social-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--border-color);
  border-radius: 12px;
  padding: 0.8rem;
  color: var(--text-color);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.3s ease;
}

.social-btn:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: var(--sub-text-color);
  transform: translateY(-2px);
}

.social-btn svg {
  opacity: 0.8;
}
</style>
