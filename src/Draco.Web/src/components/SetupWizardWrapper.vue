<template>
  <div>
    <button @click="isWizardOpen = true" class="btn" style="background: var(--dragon-red);">
      Initialize Draco Online
    </button>

    <SetupWizard v-if="isWizardOpen" @close="isWizardOpen = false" />
  </div>
</template>

<script setup>
import { ref } from 'vue';
import SetupWizard from './SetupWizard.vue';

const isWizardOpen = ref(false);
</script>
