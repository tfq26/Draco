<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { authClient } from '../lib/auth';

const session = ref<any>(null);
const isLoading = ref(true);

const checkSession = async () => {
    try {
        const { data } = await authClient.getSession();
        session.value = data;
    } catch (err) {
        session.value = null;
    } finally {
        isLoading.value = false;
    }
};

const handleSignOut = async () => {
    await authClient.signOut();
    window.location.href = '/';
};

onMounted(() => {
    checkSession();
});
</script>

<template>
  <div class="auth-status">
    <template v-if="isLoading">
      <div class="loader-dots"><span></span><span></span><span></span></div>
    </template>
    <template v-else-if="session">
      <div class="user-menu">
        <a href="/profile" class="profile-link">
          <span class="user-avatar">{{ session.user.name?.[0] || 'O' }}</span>
          <span class="user-name">{{ session.user.name }}</span>
        </a>
        <button @click="handleSignOut" class="signout-btn" aria-label="Sign Out">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4M16 17l5-5-5-5M21 12H9" />
          </svg>
        </button>
      </div>
    </template>
    <template v-else>
      <a href="/login" class="signin-btn">Login</a>
    </template>
  </div>
</template>

<style scoped>
.auth-status {
  display: flex;
  align-items: center;
}

.signin-btn {
  background: var(--dragon-red);
  color: white;
  padding: 0.6rem 1.2rem;
  border-radius: 8px;
  font-weight: 700;
  font-size: 0.9rem;
  box-shadow: 0 4px 10px var(--accent-glow);
  transition: all 0.3s ease;
}

.signin-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 6px 15px var(--accent-glow);
}

.user-menu {
  display: flex;
  align-items: center;
  gap: 1.5rem;
}

.profile-link {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  color: var(--text-color);
  font-weight: 600;
}

.user-avatar {
  width: 32px;
  height: 32px;
  background: var(--dragon-red);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  font-size: 0.8rem;
  font-weight: 900;
  border: 1px solid var(--meteo-bronze);
}

.signout-btn {
  background: none;
  border: none;
  color: var(--sub-text-color);
  cursor: pointer;
  padding: 0.25rem;
  display: flex;
  align-items: center;
  transition: color 0.2s ease;
}

.signout-btn svg {
  width: 20px;
  height: 20px;
}

.signout-btn:hover {
  color: var(--dragon-red);
}

.loader-dots {
  display: flex;
  gap: 4px;
}

.loader-dots span {
  width: 6px;
  height: 6px;
  background: var(--sub-text-color);
  border-radius: 50%;
  animation: dot-pulse 1.4s infinite ease-in-out both;
}

.loader-dots span:nth-child(1) { animation-delay: -0.32s; }
.loader-dots span:nth-child(2) { animation-delay: -0.16s; }

@keyframes dot-pulse {
  0%, 80%, 100% { transform: scale(0); }
  40% { transform: scale(1.0); }
}
</style>
